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
| UNAVAILABLE | No eligible target or broker/dependency unavailable | 503 upstream_unavailable |
| CANCELLED | Caller abandoned the request | No fabricated success; cancel local work |
| INTERNAL | Unexpected implementation failure | 502 upstream_error; redact details |

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
