# Broker transport proposal

**Status: proposed, not an approved or implemented replacement for current
transport.** Existing deployment/contracts remain authoritative until affected
owners approve the migration. The broker source is maintained separately in
`sergedbs/message-broker`.

The initial schema is [broker.proto](proto/tamagotchi/messaging/v1/broker.proto),
package `tamagotchi.messaging.v1`. Service-specific internal operation messages
are added as reviewed domain definitions, not invented by the transport.

## API and boundaries

| RPC | Caller | Result |
| --- | --- | --- |
| MessageBroker.Call | Gateway or an authenticated service | Enqueue a known destination/operation and await a correlated result within its deadline |
| MessageBroker.Publish | Authorized producer | Acknowledge only after event and subscriber-delivery state commit |
| ServiceDelivery.Execute | Broker only | Invoke an existing domain handler and return its outcome |
| ServiceDelivery.Deliver | Broker only | Commit consumer effects/deduplication before APPLIED or DUPLICATE |

Gateway retains public HTTP/JSON. Its public envelope preserves allowlisted
method/path/query/headers/body; no absolute URL, arbitrary target or secret-bearing
header may enter generic durable storage. Internal service calls use concrete
Protobuf messages carried by Any, with operation and type-URL allowlists. Opaque
bytes are not permission to tunnel internal REST/JSON. Unknown operations/types
fail closed. Broker adapters reuse application handlers, not loopback HTTP.

`message_id`, event/delivery IDs and correlation IDs are canonical UUIDv7.
`destination` is a canonical logical service name. Retries preserve message and
command identity; changed content under the same identity is a conflict. Public
Idempotency-Key is normalized to the dedicated field; repeated/conflicting values
are refused. Persisted request fingerprints exclude bearer tokens/signatures but
include verified caller, operation and validated business input.

`deadline_unix_ms` is the signed root deadline, never a budget extension.
RequestContext is untrusted until checked against the verified assertion. The
gRPC deadline can only shorten it. Expired work does not execute after reconnect.
Every admitted data-plane request uses bounded platform-thread execution; Netty
transport callbacks must not run blocking JDBC or business work.

Protobuf Timestamp values represent the existing UTC millisecond precision.
`data_json` is exactly the existing event's data object in UTF-8. Producer identity
must match the authenticated service; schema, size and publish ACLs are checked.
All envelopes are capped at 1 MiB including metadata/application overhead checks.

## Delivery semantics

Domain messages are at-least-once; each logical subscriber has independent durable
delivery state. Replicas compete within that subscriber. Guild relay needs an
expiring subscription per live Guild instance; history remains authoritative.
No global FIFO, exactly-once execution or retroactive subscription replay is
promised. Domain versions and stable effect keys remain necessary.

Delivery attempt starts at 1. Transient handler failures use initial delivery,
5-second retry, then 30-second retry, then dead letter. Permanent failures go
directly to dead letter. Missing capacity/eligible replicas defers work rather
than counting an invented handler attempt. Lease fencing is internal broker
state; a response with an expired lease cannot complete a replacement claim.
Transport loss before acknowledgement may redeliver an already committed effect.

Subscriptions persist when every replica disconnects. Discovery registration is
proposed to add an internal gRPC port/endpoint and topic/subscription metadata,
validated against a reviewed allowed-subscriptions manifest. Exact registration
fields, manifest and domain operation definitions require owner review. This PR
does not modify current registration requests or existing endpoint tables.

## Identity and bootstrap

gRPC authenticates workloads with mTLS. Gateway assertions remain destination
bound; the broker validates rather than blindly retargeting them. Internal
service tokens retain destination-scoped UM grants; verified caller and actor
context become short-lived assertions signed by the broker's own key. The exact
certificate identities, metadata keys, claims, issuers and bootstrap flow are in
[identity.md](identity.md).

Assertions/tokens use gRPC metadata, not trusted client-supplied principal fields:
`authorization`, `x-gateway-assertion` and `x-broker-context`
(`x-gateway-context` stays on the Gateway's HTTP path). Repeated security metadata is refused. Public envelope
headers exclude Authorization, secrets, assertion/context and hop-by-hop headers.
Persist only validated authorization context required by queued work.

Token issuance/bootstrap is narrowly allowlisted and cannot require the token
being issued. Credential-bearing operations, including login/token replies, use
bounded volatile queues; they are not advertised as durably accepted and must
not write credentials to the generic queue store. Direct bounded issuer JWKS
bootstrap remains an infrastructure exception.

## Error handling

Business refusals are CallReply outcomes and retain the agreed HTTP error code
for public mapping. DeliveryReply failures use bounded machine codes. Neither
counts automatically as target transport failure.

| gRPC status | Meaning | Default HTTP mapping |
| --- | --- | --- |
| INVALID_ARGUMENT | Invalid transport envelope or unknown operation/type | 400 |
| UNAUTHENTICATED | Missing/invalid workload or credential | 401 |
| PERMISSION_DENIED | Valid identity lacks required permission | 403 |
| RESOURCE_EXHAUSTED | Bounded admission or pending capacity exhausted | 503 with Retry-After: 1 |
| DEADLINE_EXCEEDED | Remaining root deadline exhausted | 504 task_timeout |
| UNAVAILABLE | No eligible target or broker/dependency unavailable | 502 upstream_unavailable |
| CANCELLED | Caller abandoned the request | No fabricated success; cancel local work |
| INTERNAL | Unexpected implementation failure | 502 upstream_error; redact details |
| ALREADY_EXISTS | Same authenticated producer/message ID, changed input | 409 message_id_conflict |

Per-attempt timeouts and root expiry are classified separately for breaker
attribution. Failover uses at most two distinct targets, remaining budget and
safe read/idempotent mutation rules; transparent library retries must not bypass
that policy. A timeout does not prove a mutation had no effect.

## Reproducible consumption

Shared is the authoritative schema source after approval. Consumer repositories
vendor reviewed `.proto` source plus its immutable Shared commit and SHA-256
manifest. Builds generate code locally with pinned Protobuf/gRPC tools and do
not fetch a moving branch. Generated code is build output, not committed source.
Before approval, consumers must label the snapshot as a proposal and link its PR;
compilation proves syntax, not owner agreement or runtime compatibility.


## Executable operation catalogue

[operations.v1.json](operations.v1.json) is the only executable allowlist, checked
against [operations.schema.json](operations.schema.json) and compiled Protobuf
descriptors. An absent operation/mapping is unavailable, not an implicit grant.
The foundation establishes the format; reviewed slices add operation mappings.
This is an incremental catalogue, not a replacement deployment manifest until
all current service operations and subscriptions have migrated.

Install the pinned tooling and run:

```sh
python3 -m pip install -r tools/requirements-contracts.txt
python3 tools/check_broker_contracts.py
python3 -m unittest discover -s tools -p 'test_*.py'
```

CI compiles every schema and validates shape, duplicate operations/routes,
caller identities, concrete type URLs, header rules, retry policy and credential
storage. The existing documentation checker does not replace these checks.
Build tools generate code; generated files/descriptors stay outside Git.

A mapping declares durable/volatile storage for both input and reply, credential
presence, safe-read/idempotent/unsafe failover, command-key/natural enforcement,
and request/reply byte bounds. Each complete serialized transport envelope is
also limited to 1 MiB. Reject oversized public input with 413 payload_too_large;
reject oversized upstream replies with a redacted 502, never truncate. Per-route
limits do not allow body bytes plus unbounded metadata.

One operation can expose public and typed internal mappings, each with its own
access policy. Gateway's authenticated workload may carry a verified user or
anonymous public context. Internal service identities need their listed exact
scopes. No client field can switch authorization modes. Domain ownership/self
checks still run inside the destination application. A listed scope describes
a required grant; it does not itself issue a token or modify UM's allowlist.

Operation names are stable identifiers. Destination is a separate canonical
service field; the selected users.* and registry.* prefixes are not alternate
service identities. Internal type URLs must name compiled, versioned messages.
No opaque internal JSON, arbitrary URL dispatch or loopback HTTP adapter.

Request headers are explicitly allowlisted per route. Idempotency-Key is carried
only in CallRequest.idempotency_key. Strip credentials, assertions, caller identity,
hop-by-hop headers and every header nominated by Connection before persistence.
Security metadata is separately verified, never copied into the durable envelope.
Response headers preserve Content-Type, ETag, Retry-After, WWW-Authenticate and
other explicitly permitted metadata, including empty 304 responses. Prohibit
Set-Cookie and all credential-bearing metadata in generic durable replies.

PublicHttpRequest.path is the original raw path after Gateway prefix removal,
matched against the allowlisted template. Refuse malformed escapes, encoded slash
or backslash separators (case insensitive), literal backslashes, dot segments
and double slashes. Decode route parameters once; reject residual encodings that
could alter routing on a second decode. Gateway, broker and adapter must apply
the same rules. Query keys and repeated-value rules follow the destination's
existing API; unknown keys are refused. This document defines required behavior,
not an already installed Gateway runtime validator.

No eligible target maps to 502 upstream_unavailable, bounded admission to 503
too_many_tasks with Retry-After: 1, root expiry to 504 task_timeout. Business
responses preserve their existing HTTP body/status/headers. Unexpected broker
failure maps to redacted 502 upstream_error. Do not infer retry safety from status
alone or blame target transport for an ordinary domain/dependency response.

A fresh application invocation receives a fresh message UUIDv7; transport retries
of it keep that ID. Same ID plus changed stable input is 409 message_id_conflict.
Same ID plus identical pending input joins the existing deadline-bounded waiter;
it never executes another copy. Preserve current authorization before lookup.
Domain command/effect keys and receipts remain separate and authoritative.

## Capacity deferral and delivery ownership

DeliveryReply.DEFERRED (5), reason capacity_unavailable, is a verified destination
refusal before handler execution: no effects and no handler attempt. It is not
success, DUPLICATE, a transient handler failure or target-breaker evidence.
Do not classify bare RESOURCE_EXHAUSTED as this outcome: transport libraries also
use that status for message-size and unrelated resource failures.

The current DeliveryStore.startAttempt marks dispatch and increments attempts;
DeliveryStore.defer only accepts a lease with no dispatch marker. Therefore the
worker cannot pass a returned DEFERRED directly to that existing defer method.
The delivery owner must add a fenced not-executed settlement path: verify the live
lease and recorded dispatch, undo only this refused dispatch's provisional attempt
and target reservation, requeue with a bounded delay and keep capacity reserved.
It must be idempotent and must not roll back any prior uncertain/failed attempt.
Cancellation, transport loss or missing acknowledgement cannot prove non-execution
and must use the uncertain-delivery policy. Do not clear dispatch markers after
an ambiguous outcome. Test refusal, stale leases and duplicate settlement with
real PostgreSQL. This required integration is not implemented by the enum change.

## Direct and volatile exceptions

Map demo assets remain narrowly allowlisted direct forwarding with existing
Discovery selection. They do not enter durable queues or need a fixed Map target.
Probes, Discovery, issuer JWKS, metrics, providers and negotiated Guild sockets
remain documented infrastructure paths, not general internal HTTP fallbacks.

Preserve UM's existing verified admin issuance alternative as a volatile public
mapping when its security adapter lands. Workload issuance is separately
allowlisted and cannot require the token it issues. Invalid service credentials
never fall back to admin. Login, refresh, logout, registration, device registration
and ticket operations need explicit secret-bearing request/reply classification.
No missing identity fields or schemas are invented by this catalogue foundation.

## First migration slice

The catalogue now contains four Map public mappings, typed
users.list_relationships (destination user-management, caller map,
users:read-relationships), and Victoria's eleven Tamagotchi/eight Notification
public mappings. Public routes keep the payloads, self/holder checks, errors and
pagination defined in the main README and docs/field-types.md. These are approved
migration targets after review, not claims that gRPC adapters are deployed.
Default bounds are 16 KiB per request and 64 KiB per reply. Creature and
collection pages allow 768 KiB, device/notification pages 128 KiB, holders/types/
matrix 64 KiB, and Map nearby pages 512 KiB. Creature pages at the current
32-stat maximum exceed 512 KiB. These payload limits remain subject to the
complete 1 MiB envelope cap; maximum-size fixtures must pass before cutover.
Measured worst cases of a 100 item page are 26 KiB (devices), 55 KiB (notifications)
and 532 KiB (creatures).

Relationships use tamagotchi.user_management.v1.ListRelationshipsRequest/Reply.
Request user_id and item user_id are canonical UUIDv7. Limit is 1..100. A cursor
is absent for the first page or opaque nonempty text of at most 2048 characters.
Reply items are bounded to 100, enum values must be FRIEND/ENEMY, and absent
next_cursor corresponds to JSON null. Empty-present cursors and unknown enum
values are invalid dependency responses. Do not classify missing users as strangers
until all pages were fetched successfully. Same-user duplicates with conflicting
relationships and repeated/cyclic cursors are refused by the caller.

Map's public writes, reads, deletion and nearby queries remain self-only.
Upsert requires its existing command key; reads are safe; deletion stays unsafe
for automatic failover because a repeat changes its response. Completed command
replay still returns before calling User Management. Preserve the current
six-metre stranger-only partial response on relationship failure. Existing Map
JSON timestamps, fingerprints and cursor bindings are unchanged.

## Tamagotchi internal operations

Package tamagotchi.tamagotchi.v1 defines the service-only routes and the service
reads of Tamagotchi. Callers and scopes match the issuer allowlist in the main README.

| Operation | Callers and scope | Retry |
| --- | --- | --- |
| tamagotchi.award_xp | battle, monster-raid: tamagotchi:award-xp | command key |
| tamagotchi.grant_access | battle: tamagotchi:grant-access | command key |
| tamagotchi.reserve_engagement | battle, monster-raid: tamagotchi:reserve-engagement | command key |
| tamagotchi.release_engagement | battle, monster-raid: tamagotchi:release-engagement | command key |
| tamagotchi.get_engagement | battle, monster-raid: tamagotchi:read-engagement | safe read |
| tamagotchi.list_holders | battle: tamagotchi:read-holders | safe read |
| tamagotchi.get_collection | monster-raid: tamagotchi:read-collection | safe read |

list_holders and get_collection keep their public user mapping and add the typed
service mapping. The caller kind is taken from the verified identity, never from a
request field. Creating a creature (tamagotchi:mint) and reading one as a service
(tamagotchi:read-creature) have no caller and stay unmapped.

The command key is the CallRequest idempotency_key, 1 to 128 printable ASCII
characters, and Tamagotchi keeps its 24 hour receipts. A replay returns the stored
reply. Ids are canonical UUIDv7 strings, versions are 1..2147483647, timestamps are
UTC with millisecond precision, xp is at most 2^53-1. Absent cursor starts the first
page and a present cursor is 1..2048 characters. An absent primary or next_cursor
corresponds to JSON null. Unspecified enum values are refused by the adapter.
package_stats values are number, text or flag, with keys of 1..64 characters.

Business refusals are call failures with the HTTP status and code of the public
API, not typed replies: 400 invalid_xp, invalid_grant, unknown_engagement,
invalid_lineup; 404 unknown_tamagotchi, unknown_creature, unknown_engagement;
409 creature_engaged, reference_in_use. ALREADY_HOLDER and CAP_REACHED are
successful GrantAccessReply outcomes and never count against the target.

The manifest retains every existing durable event/subscription from the deployed
topology, including all six Notification topics and bounded offline audit work.
Audit is not a new authenticated service or claimed online consumer. Guild's
per-instance chat relay is not invented here; its reviewed replacement topic and
adapter remain required before replacing the current relay. Deployment still uses
its current topology until the entire replacement passes acceptance.

UM bootstrap/credits, Guild/Registry operations and Raid routes must be supplied
by their owners before those paths are enabled. Public entries do not implicitly
expose their service-only routes. No concrete security claim names, bootstrap
messages or Discovery registration fields are invented in this slice.
Notification mark_read is IDEMPOTENT by NATURAL basis, not by a command key: the
update keeps the first read_at atomically, a repeat returns the same record and the
operation has no other effect (victoriaaa111/notifications#51, with 20 overlapping marks
against PostgreSQL). The ownership check runs on every invocation, the two-target limit
and the remaining-deadline rule still apply, and the If-Match writes stay UNSAFE. The
policy may be applied only when every selectable Notification replica runs an image
that contains that change. Notification images up to 3.0.1 do not, so the first image
pinned in Compose after them is the earliest one failover may use.

## User Management service token issuance

`users.issue_service_token` is the bootstrap operation from
[identity.md](identity.md) section 5. Its internal mapping uses
`tamagotchi.user_management.v1.IssueServiceTokenRequest` and
`IssueServiceTokenReply`, with the fields of ServiceTokenRequest and ServiceToken.
Callers are battle, guild, map, monster-raid and tamagotchi, each with an empty
scope list: the caller proves itself with its certificate and `x-service-secret`
metadata, never a bearer token. User Management keeps its allowlist and its
secret check; the broker only requires `service_name` to equal the certificate.

The verified admin alternative is the separate public operation
`users.issue_service_token_as_admin`, `POST /v1/service-tokens` with `admin`
access, so the two ways of proving identity never share a mapping. Both
operations are VOLATILE and contain credentials, and both are UNSAFE: a lost
reply is not retried on another replica. A wrong secret never falls back to admin. Package Registry and
Notification have no grants and do not call it; User Management signs its own
Registry token in-process.

## User Management internal operations

Package tamagotchi.user_management.v1 defines User Management's service-only
routes and service reads. Callers and scopes match User Management's issuer
allowlist (`service-token-policy.json`, audience user-management); the README's
issuer table lists only the Battle, Map and Monster Raid rows of it.

| Operation | Callers and scope | Retry | Messages |
| --- | --- | --- | --- |
| users.read_profile | battle: users:read-profile | safe read | GetUserProfileRequest, UserProfile |
| users.settle_battle | battle: users:settle-battle | command key | SettleBattleRequest, BattleSettlement |
| users.consume_boost | battle: users:consume-boost | command key | ConsumeBoostRequest, BoostReceipt |
| users.credit_global | battle, monster-raid: users:credit-global | command key | CreditGlobalRequest, CreditReceipt |
| users.credit_local | tamagotchi: users:credit-local | command key | CreditLocalRequest, CreditReceipt |
| users.get_relationship | guild: users:check-relationship | safe read | GetRelationshipRequest, GetRelationshipReply |
| users.get_membership | guild: users:read-membership | safe read | GetMembershipRequest, MembershipSnapshot |

Each message carries the fields of its JSON shape in `docs/field-types.md`, and a
route's path ids become request fields (`user_id`, `other_user_id`). The service
read of a profile is `users.read_profile`, separate from the public
`users.get_profile`: only Tamagotchi's two reads keep both mappings on one
operation. The caller kind comes from the verified identity, never from a field.

The command key is the CallRequest idempotency_key and User Management keeps its
existing receipts, so a replay returns the stored reply. Ids are canonical UUIDv7
strings, amounts and balances are at most 2^53-1, versions 1..2147483647.
`BoostId`, `CreditReason` and the relationship `Kind` start with an UNSPECIFIED
value that adapters refuse. The local credit `action` stays a string because each
package defines its care actions. Business refusals are call failures with the
public API's status and code (for example 404 user_not_found, 422 no_charges,
422 unknown_care_action), not typed replies.

`users:read-wallet` has no service caller and stays unmapped. User Management's own
call to Package Registry (`registry:read-config`) belongs to Registry's slice.

## Package Registry internal operations

Package tamagotchi.package_registry.v1 defines Package Registry's service reads. Every
caller needs `registry:read-config`, the scope of the matching HTTP routes, and each is a
safe read. User Management signs its own Registry token in process.

| Operation | Callers | Messages |
| --- | --- | --- |
| registry.get_stat_definitions | battle, tamagotchi | GetStatDefinitionsRequest, GetStatDefinitionsResponse |
| registry.get_stat_bonuses | battle, tamagotchi | GetStatBonusesRequest, GetStatBonusesResponse |
| registry.get_currency_rules | tamagotchi, user-management | GetCurrencyRulesRequest, GetCurrencyRulesResponse |
| registry.get_starter_pet | tamagotchi | GetStarterPetRequest, GetStarterPetResponse |
| registry.read_package | user-management | ReadPackageRequest, ReadPackageResponse |

`registry.read_package` is the service read of a package, separate from the public
anonymous `registry.get_package`: only Tamagotchi's two reads keep both mappings on one
operation. `config_version` 0 in a config request means the current version. Business
refusals are call failures with the public API's status and code (for example 404
package_not_found, 400 validation_error).

## User Management and Battle public routes

The public routes of User Management (`users.*`) and Battle (`battle.*`), so the
Gateway can send them through the broker. Paths, methods, access and query keys
come from each service's endpoint code; the self, participant and scope checks
stay inside the service. With Gateway dispatch on, a route that is not listed is
a 404, so these must be listed before the cutover. Each service still needs its
public HTTP adapter behind `ServiceDelivery.Execute` before the Gateway sends
them there; until then the Gateway's pinned catalogue keeps them direct.

| Policy | Routes |
| --- | --- |
| VOLATILE, contains credentials, anonymous, reply at most 16 KiB | `users.login`, `users.refresh`, `users.logout`, `users.register` |
| IDEMPOTENT with `COMMAND_KEY` (the service requires `Idempotency-Key`) | `users.register`, `users.join_package`, `users.create_friend_request`, `users.accept_friend_request`, `users.reject_friend_request`, `battle.create_battle`, `battle.accept_battle`, `battle.reject_battle`, `battle.attack`, `battle.forfeit_battle` |
| SAFE_READ | `users.get_me`, `users.get_profile`, `users.list_my_boosts`, `users.list_friend_requests`, `users.get_global_wallet`, `users.get_local_wallet`, `battle.list_battles`, `battle.get_battle` |
| UNSAFE, never resent | `users.login`, `users.refresh`, `users.logout`, `users.mark_enemy`, `users.remove_enemy`, `users.remove_friend` |

A second `remove_enemy` or `remove_friend` answers 404, so resending one after an
uncertain attempt would mislead the client. `mark_enemy` and `logout` repeat
harmlessly, but stay UNSAFE for now: the catalogue keeps `NATURAL` to
`notification.mark_read`. Pages take only `limit` and `cursor` (at most 100
items); reply bounds are 128 KiB for User Management pages and 256 KiB for
`battle.list_battles`, about twice a full page. Only the routes that answer 201
keep `location`.

Not in this slice:
- the service-only routes (`/v1/internal/*`, currency credits, membership and the
  relationship check), and the service callers of `users.get_profile` and the
  global wallet, which need typed internal messages;
- the public mapping of `users.list_relationships`, which stays internal;
- issuer JWKS, probes and `/status`, which are infrastructure paths.
