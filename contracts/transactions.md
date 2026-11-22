# Coordinated battle settlement (proposal)

Status: proposed, not an implemented or approved transport. Participant owners
review these types and scopes before adapters depend on them. Existing operations
and deployed snapshots remain unchanged. The saga owner reviews SagaView and the
saga definition in a coordinated follow-up; an unimplemented StartSaga must return
UNIMPLEMENTED, never acknowledge work it cannot durably execute.

## Admission, identity and replay

TransactionCoordinator uses the Broker's existing TLS1.3 listener and workload
certificates. Only `battle` starts the fixed `battle-settlement` definition.
`battle`, `user-management` and `tamagotchi` can recover decisions. No public
HTTP mapping, arbitrary operation list, caller-selected scope or participant URL
is accepted. UUID fields are UUIDv7. Correlation survives background processing.

A start commits its plan and returns PREPARING (or saga RUNNING) promptly; no
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

Saga ordering, retry/pivot/deadline behavior is finalized by the saga owner before
this proposal is approved. Both modes use the same participant dispatch boundary.

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

Test async starts, changed-plan conflicts, uncertain start replay, two prepare
waves, repeated actual votes, vote NO, late prepares, restart around each decision
boundary, prepared-transaction failover, phase-two lost replies and authoritative
absence races. Component doubles are distinct from real participant acceptance.
