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
context become short-lived assertions signed by the broker's own key. Exact
claim/type/issuer definitions are a separate affected-owner review gate.

Assertions/tokens use gRPC metadata, not trusted client-supplied principal fields:
`authorization`, `x-gateway-assertion`, `x-gateway-context` or the reviewed broker
assertion equivalent. Repeated security metadata is refused. Public envelope
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
The initial foundation has no operations; subsequent reviewed slices add them.
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
