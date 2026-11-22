# Coordinated battle settlement (proposal)

Status: proposed, not an implemented or approved transport. Participant owners
review these types and scopes before adapters depend on them. Existing operations
and deployed snapshots remain unchanged. The saga definition, SagaView and the saga
rules are in [Saga: battle-settlement](#saga-battle-settlement). An unimplemented
StartSaga must return UNIMPLEMENTED, never acknowledge work it cannot durably execute.

## Admission, identity and replay

TransactionCoordinator uses the Broker's existing TLS1.3 listener and workload
certificates. Only `battle` starts the fixed `battle-settlement` definition.
`battle`, `user-management` and `tamagotchi` can recover decisions. No public
HTTP mapping, arbitrary operation list, caller-selected scope or participant URL
is accepted. UUID fields are UUIDv7. Correlation survives background processing.

A start commits its plan and returns PREPARING (or SAGA_RUNNING) promptly; no
participant call runs on the inbound request budget. Disconnect after admission
does not cancel durable settlement. Same ID and normalized input returns current
state; changed input is ALREADY_EXISTS. Durable state contains no credentials.

The compared input is the normalized settlement plan and, for 2PC, access_step.
correlation_id is validated on every start but is not part of that fingerprint:
an identical retry may carry a fresh correlation ID and still joins the original
work. Background calls and the stored view retain the first correlation ID.

One settlement mode owns a battle. Admission in both modes atomically takes the
same battle-level lock before checking either mode's durable records. Starting
the other mode returns FAILED_PRECONDITION settlement_mode_conflict, including
when the previous attempt has aborted; a mode change needs explicit reconciliation.
While saga execution is unavailable, select 2pc only after its runtime passes
acceptance. StartSaga remains UNIMPLEMENTED; there is no automatic mode fallback.

The fixed coordinator definition delegates only its approved operations as
`service:battle`. Each dispatch derives its destination/scope from the reviewed
catalogue and signs a fresh, instance-bound Broker context. It never persists a
service token or accepts caller-supplied delegation claims. This narrow extension
requires security-owner review; ordinary Call authentication is unchanged.

New `users:transactions`, `tamagotchi:transactions` and `battle:transactions`
permissions are proposals, granted only to Battle for these fixed participant
operations. They confer no generic write permission. UM's issuer policy and owner
adapters must be reviewed together; no existing grant is broadened silently.

## Plans and two-phase commit

Plan names the battle, distinct winner/loser, optional engagement, winner's two
creatures, staked creature, 60/40 XP and fixed access-cap credit 50. Battle supplies
GRANT or CREDIT_INSTEAD from its holder pre-read. Transaction ID is battle UUID
first, then `.1`, `.2`, etc. after confirmed aborts; store that counter before
starting. Maximum ID length is 150 ASCII letters/digits or `._:-`, starting with a
letter/digit. GID appends `:<service>` and stays below 200 bytes.

1. Prepare UM and Tamagotchi concurrently (2.5s per attempt). Both vote YES or
   explicitly refuse. Unknown/timeout cannot authorize COMMIT.
2. After their YES votes, prepare Battle with their actual access outcome. Do not
   guess GRANTED. A repeated prepare must return the same vote and outcome; an
   empty YES is insufficient to derive Battle's result.
3. Commit the immutable COMMIT or ABORT decision in Broker storage before phase
   two. A no vote or preparation deadline aborts. Each admitted transaction has
   one decision; no new attempt starts while its earlier result is unresolved.
4. Repeat stable commit/abort commands until acknowledged, with bounded attempts
   and capped backoff. Alert on unresolved recovery, retain the decision and work.
   UNKNOWN commit or contradictory outcome is NEEDS_ATTENTION, not success.

A participant uses its own DB connection/transaction, writes its outcome and
PREPARE TRANSACTION before voting YES. Do not hold a domain transaction while
calling another service. Repeated commit replays its stored outcome; absence of
both prepared and committed state is UNKNOWN. Never write another owner's DB.

GetDecision returns PENDING, COMMIT or ABORT. Missing IDs produce an atomic,
durable presumed-ABORT tombstone, preventing a late start for that ID. Unavailable
storage/auth/transport returns an error, never presumed abort. Late prepare after
an abort must be fenced or recovered against that durable decision; an early
NOTHING_TO_ABORT must not conceal a subsequently prepared transaction.

In particular, NOTHING_TO_ABORT counts as a phase-two acknowledgement only when
the participant durably records an abort fence under the same serialization used
by prepare. A later prepare must refuse that ID. Recovery scans prepared work at
startup and every five seconds, querying the authoritative decision without an
age delay; sixty seconds is a warning threshold, not permission to roll back or
the time before recovery starts. Each pass is bounded and failed lookups retry
on the next pass. A participant without the abort fence must not claim final
resolution merely because an earlier abort found no work.

Stable effect keys are `{id}:{step}:{phase}`; persist one UUIDv7 Call message ID per
logical action and reuse it after uncertain responses. New dispatch attempts get
fresh deadlines and contexts; root start deadline is not the workflow lifetime.

## Saga and participant results

SettlementOutcome is SETTLED or COMPENSATED. AccessOutcome is GRANTED,
ALREADY_HOLDER, CAP_COMPENSATED or NO_ACCESS; UNSPECIFIED is invalid for terminal
results. A compensated payout cannot claim that access was granted. Battle's
mark/prepare uses the verified result and publishes its completed event through
its existing outbox only once.

SETTLED + NO_ACCESS is invalid for this fixed definition: successful settlement
has GRANTED, ALREADY_HOLDER or CAP_COMPENSATED. COMPENSATED + NO_ACCESS represents
a compensated forward path; an uncertain pivot has no terminal settlement claim.

Currency reversal refunds the original debit to the loser, takes the available
winner credit back and reports `unrecovered`; balances remain unsigned. Reversal
of an unsettled battle records a tombstone and moves nothing. A late settlement
is `409 battle_settlement_reversed`. Infrastructure failures remain failures;
partial unrecovered money is visible reconciliation data, not a fully restored
balance. XP reversal identifies the existing BATTLE effect and recomputes level.

Both modes use the same participant dispatch boundary. One battle is settled by
exactly one mode, as stated under Admission (`settlement_mode_conflict`).

## Saga: battle-settlement

The release default. Saga ID is the battle UUID, never `.N`: a saga does not abort and
restart, it compensates. The fixed definition, in order:

| # | Step | Operation | Kind | Compensation |
| --- | --- | --- | --- | --- |
| 1 | `settle_currency` | `users.settle_battle` | COMPENSATABLE | `users.reverse_settlement` |
| 2 | `award_xp_primary` | `tamagotchi.award_xp` (winner primary, 60, BATTLE, battle ID) | COMPENSATABLE | `tamagotchi.revoke_xp` |
| 3 | `award_xp_secondary` | `tamagotchi.award_xp` (winner secondary, 40, BATTLE, battle ID) | COMPENSATABLE | `tamagotchi.revoke_xp` |
| 4 | `grant_access` | `tamagotchi.grant_access` (staked creature, winner, BATTLE_LOSS) | PIVOT | none: access is never revoked |
| 5 | `credit_access_cap` | `users.credit_global` (winner, 50, `BATTLE_ACCESS_CAP`, battle ID) | RETRIABLE | none; SKIPPED unless step 4 answered CAP_REACHED |
| 6 | `release_engagement` | `tamagotchi.release_engagement` | FINALIZER | none; SKIPPED when the plan has no engagement |
| 7 | `mark_settled` | `battle.mark_settled` (outcome, access outcome) | FINALIZER | none; always last |

**Forward path.** Steps 1-5 run one at a time, then the finalizers. Access outcome is
step 4's answer: GRANTED or ALREADY_HOLDER, or CAP_COMPENSATED once step 5 succeeds.
The saga ends COMPLETED with outcome SETTLED.

**Compensation path.** When a step before the pivot fails, the compensatable steps that
completed or may have completed are compensated in reverse order (3, 2, 1), then the
finalizers run. The saga ends COMPENSATED with outcome COMPENSATED and access NO_ACCESS;
`mark_settled` records exactly that. After the pivot has succeeded the saga never
compensates.

**What counts as a failure.** A participant's definite business refusal is a failure at
once. A transport failure, timeout or uncertain reply is retried with the same effect
key, because every step operation is IDEMPOTENT with COMMAND_KEY and a retry returns the
stored result. Per step kind:

| Kind | Attempts | Then |
| --- | --- | --- |
| COMPENSATABLE | 3 | Failed: compensate. The step itself is compensated too, because it may have applied. |
| PIVOT | until a definite answer, within the forward deadline | A definite refusal compensates. An uncertain pivot is **never** compensated: past the deadline the saga becomes SAGA_NEEDS_ATTENTION. |
| RETRIABLE, FINALIZER, compensations | until they succeed, up to 10 minutes per step | SAGA_NEEDS_ATTENTION |

Attempts get a 2.5 s timeout and backoff 1, 2, 4, 8 s, capped at 30 s. Each attempt has a
fresh deadline and context, the same effect key `{saga_id}:{step}:{execute|compensate}`
and the same persisted Call message ID for its logical action.

**Deadline.** The forward deadline is 10 minutes after admission (`deadline_at`). A saga
that has not passed the pivot by then compensates. Compensation and finalizers are not
bound by it; they have their own 10 minutes per step.

**Compensations must be safe without the forward step.** A compensation can arrive for a
step whose forward call never applied, or before a late forward call arrives:

- `users.reverse_settlement` for an unsettled battle records a tombstone and moves
  nothing; a later settlement is `409 battle_settlement_reversed` (as above).
- `tamagotchi.revoke_xp` for an effect that does not exist records a tombstone for
  `(tamagotchi_id, BATTLE, battle ID)` and succeeds with nothing changed; a later
  `award_xp` with that key is `409 xp_award_revoked`. A repeated revoke returns its
  first result.

**Durability and restart.** StartSaga commits the saga and its plan, answers
SAGA_RUNNING and returns; no participant call runs on the inbound request budget. Each
step's state, attempts and Call message ID are written before and after every call, in
the Broker database (`sagas`, `saga_steps`). After a restart the Broker resumes every
non-terminal saga at its current step and resends an in-flight call with the same key.
Same ID and plan returns the current SagaView; a changed plan is ALREADY_EXISTS.

**SAGA_NEEDS_ATTENTION** is terminal for automation: retries stop and the saga keeps its
last step states for an operator. When it is reached because the pivot stayed uncertain,
`release_engagement` still runs first (releasing is safe whatever the pivot did), but
`mark_settled` does not, because the outcome is unknown. Battle's status check sees it and records
`NEEDS_ATTENTION`, as it does today for a settlement half that keeps failing.

**Demo faults.** A `fail` fault answers `502 fault_injected`, which the saga treats as
uncertain and retries with the same key. To show a compensation, inject it with `count` 3
on a compensatable step (for example `tamagotchi.award_xp`), so all three attempts fail.
A `delay` fault longer than the 2.5 s attempt timeout makes the attempts time out instead.

**Observability.** `sagas_total{definition,outcome}`, `saga_step_attempts_total{step,result}`
and `saga_compensations_total{step}`. Log lines `saga started`, `saga step failed`,
`saga compensating`, `saga finished` with `saga_id`, `step`, `attempt` and `reason`,
never request or reply bodies.

## Management and acceptance

### Controlled participant faults

Participants expose POST /v1/admin/faults directly on their service HTTP port,
protected by current admin authorization, never as a public Gateway mapping.
The body is {operation, mode, delay_ms, count}. Only the service's explicit
operation allowlist is accepted; faults are durable and count is consumed
atomically across replicas. count defaults to 1 and is bounded to 1..100.
delay requires 1..30000 milliseconds and honors cancellation before running the
operation. fail returns 502 fault_injected. vote_no on a tx_prepare operation is
a normal typed negative vote with reason fault_injected, without a prepared
effect; on other operations it behaves as fail. The demo uses the loopback-only
ports in compose.demo.yaml and a real admin credential. Ordinary deployment
does not expose these administration ports publicly.

Existing admin authorization protects GET /v1/transactions[/{id}] and
GET /v1/sagas[/{id}]. Responses expose progress/reasons, not credentials or bodies.
`GET /v1/sagas` takes `state` (a SagaState name), `limit` (1-100, default 20) and an
opaque `before` cursor, and answers `{"items": [SagaView], "next_before": …}` newest
first; `GET /v1/sagas/{id}` answers one SagaView with every step. JSON field names are
the proto names; enum values are their names.

Test async starts, changed-plan conflicts, uncertain start replay, two prepare
waves, repeated actual votes, vote NO, late prepares, restart around each decision
boundary, prepared-transaction failover, phase-two lost replies and authoritative
absence races. Component doubles are distinct from real participant acceptance.

Saga acceptance: the happy path with GRANTED, ALREADY_HOLDER and CAP_REACHED; a refusal
at each compensatable step and at the pivot; an uncertain pivot reaching the deadline;
a compensation arriving before its late forward call; a Broker restart before, during
and after the pivot; a lost reply at every step; mode conflict with 2PC; and the
finalizers running on both paths.
