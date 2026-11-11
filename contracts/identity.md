# Broker identity and bootstrap

**Status: proposed for review on sergedbs/message-broker#7.** It defines the exact
names that the broker, the Gateway and every service adapter use. Nothing here is
deployed yet; the current HTTP assertion path stays authoritative until cutover.

The design reuses what services already verify. The broker's destination context
has the same claims as today's `X-Gateway-Assertion`, with its own issuer, type
and key, so a service adapter can map it to the caller identity its HTTP
middleware already builds.

Canonical service names are the Gateway's `CANONICAL_SERVICE` values:
`user-management`, `tamagotchi`, `battle`, `guild`, `package-registry`, `map`,
`monster-raid`, `notification`. The two infrastructure workloads are `gateway`
and `message-broker`.

## 1. Workload identity (mTLS)

| Item | Rule |
| --- | --- |
| CA | One local CA for the stack. Only its certificate is mounted into containers; its private key is never mounted |
| Certificate | One per logical workload, shared by all its replicas. RSA 2048+ or ECDSA P-256 |
| Identity | Exactly one URI SAN `spiffe://tamagotchi-go/workload/<name>`, where `<name>` is a canonical name above. Anything else is refused |
| Hostname | A DNS SAN with the compose service hostname, so the client can verify the server |
| Protocol | TLS 1.3 only. No plaintext gRPC listener |
| Files | `TLS_CA_PATH`, `TLS_CERT_PATH`, `TLS_KEY_PATH` (mounted read-only) |

The broker's `MessageBroker` port requires a client certificate. A service's
`ServiceDelivery` port requires a client certificate whose identity is
`message-broker`; any other peer gets `UNAUTHENTICATED` before the request is read.

## 2. Security metadata

gRPC metadata keys are lowercase. Each key below may appear at most once.
Repeated, empty or unexpected security keys are refused with `UNAUTHENTICATED`.
No security value is ever copied into the stored envelope, a reply or a log line.

| Key | Sent by | On | Value |
| --- | --- | --- | --- |
| `x-gateway-assertion` | gateway | `MessageBroker.Call` | Today's Gateway assertion, unchanged |
| `authorization` | a service | `MessageBroker.Call` | `Bearer <UM service token>` for the destination |
| `x-broker-context` | a service | `MessageBroker.Call` (optional) | The broker context it received for the work it is doing |
| `x-service-secret` | a service | `MessageBroker.Call` for `user-management.issue_service_token` only | The existing service secret |
| `x-broker-context` | message-broker | `ServiceDelivery.Execute` and `Deliver` | The broker context in section 4 |

`MessageBroker.Publish` needs only the workload certificate: the producer must
equal the certificate identity, and the catalogue's publish ACL must allow the
topic. No bearer token is needed or accepted.

`x-gateway-context` is not accepted by the broker. It stays a Gateway-only
header for today's HTTP path.

## 3. What the broker checks at admission

**Gateway calls.** The certificate identity must be `gateway`, and
`x-gateway-assertion` must be present. The broker applies the same rules that
User Management's `GatewayAssertionVerifier` applies today:

- `alg` RS256, `typ` `gateway-assertion+jwt`, `kid` found in the Gateway JWKS
  (`GATEWAY_ASSERTION_JWKS_PATH`, mounted)
- `iss` `tamagotchi-go-gateway`, `aud` equal to `CallRequest.destination`
- integer `iat` and `exp`, with 0 < `exp` − `iat` ≤ 30 s, not expired (5 s leeway)
- `jti` a UUID, `correlation_id` equal to `RequestContext.correlation_id`
- `principal_kind`, `sub`, `roles` and `scope` agree, as in the Gateway README

**Gateway-only typed operations.** `guild.issue_chat_ticket` is an internal
mapping, called only by the authenticated `gateway` workload with a verified
Gateway assertion for `guild` and `principal_kind` `user`. Guild checks current
membership before issuing the ticket. The catalogue's `guild:issue-chat-ticket`
label identifies this capability; it does not require a UM service token or a
scope in the user's assertion, and it grants no authorization by itself.
No other internal operation accepts a Gateway user assertion unless its agreed
contract explicitly permits that caller and principal. Browser access remains
`POST /gateway/v1/ws-negotiate`; no public ticket mapping is exposed.

**Service calls.** The certificate identity must be a domain service, and
`authorization` must hold a User Management access token:

- `alg` RS256, `typ` `at+jwt`, key from UM's JWKS (fetched directly; this is the
  existing infrastructure exception)
- `iss` `tamagotchi-go-users`, `sub` `service:<certificate identity>`, `aud`
  equal to `CallRequest.destination`
- 0 < `exp` − `iat` ≤ 300 s, not expired (5 s leeway)
- `scope` contains the scope the operation catalogue lists for that caller

If the service also sends `x-broker-context`, the broker verifies its own
signature (section 4) with `aud` equal to the calling service. It then keeps
that context's correlation ID, shortens the deadline to it, and carries its user
as `actor`. A carried actor is trace and self-check context. It grants the
service no extra permission.

**Effective deadline.** The broker uses the smallest of: the claim's
`deadline_unix_ms`, `RequestContext.deadline_unix_ms` and the gRPC deadline. A
value can only shorten it.

**What is stored.** Only the verified result: caller workload, `principal_kind`,
`sub`, `roles`, `scope`, `actor`, correlation ID and deadline. Raw assertions,
tokens and secrets are not stored. Today's `AuthorizationSnapshot` holds caller,
subject, audience and scopes, so `principal_kind`, `roles` and `actor` still
need adding there (to agree with @sergedbs).

Before a stored reply is replayed, the broker requires the same verified caller
and subject as the original admission.

## 4. Broker destination context (`x-broker-context`)

The broker signs one context per delivery attempt, when it dispatches, after the
claim. Time spent in the queue therefore never uses up the context's lifetime.
The root deadline still applies.

Header: `alg` RS256, `typ` `broker-context+jwt`, `kid`.

| Claim | Type | Meaning |
| --- | --- | --- |
| `iss` | string | `tamagotchi-go-broker` |
| `aud` | string | Destination canonical name |
| `principal_kind` | string | `user`, `service` or `anonymous`: whom the destination authorizes |
| `sub` | string | User UUID, `service:<name>`, or `anonymous` |
| `roles` | string[] | The user's roles; `[]` for service and anonymous |
| `scope` | string | Space-separated scopes from the service token; `""` for user and anonymous |
| `actor` | object, optional | `{"user_id", "roles"}`, only with `principal_kind` `service` when a verified user was carried |
| `caller` | string | Workload that called the broker: `gateway` or a service name |
| `instance_id` | string | `selected_instance_id`: the replica this attempt is meant for |
| `message_id` | string | `CallRequest.message_id`, or `EventEnvelope.event_id` for a delivery |
| `operation` | string | The catalogue operation (Execute only) |
| `topic` | string | The event topic (Deliver only) |
| `correlation_id` | string | Root correlation ID |
| `deadline_unix_ms` | integer | Effective call deadline, or this delivery attempt's deadline |
| `jti` | string | Fresh UUIDv7 per attempt |
| `iat`, `exp` | integer | 0 < `exp` − `iat` ≤ 30 s, and `exp` is not after the deadline (rounded up to a second) |

Public Gateway calls copy `principal_kind`, `sub`, `roles` and `scope` from the
verified Gateway assertion. A service call has `principal_kind` `service`, `sub`
`service:<caller>` and the token's scopes. An event delivery has `service:<producer>`
and an empty scope.

### What a service adapter checks

These checks are the answer for every `ServiceDelivery` adapter, including
Guild and Package Registry:

1. The TLS peer is `message-broker`.
2. There is exactly one `x-broker-context`.
3. `alg` RS256, `typ` `broker-context+jwt`, and `kid` is in the broker JWKS
   (`BROKER_CONTEXT_JWKS_PATH`, mounted; reread the file at most every 60 s, as
   the Gateway does for its key).
4. `iss` `tamagotchi-go-broker`, `aud` equal to the service's own name, integer
   `iat` and `exp`, a lifetime of at most 30 s, and not expired (5 s leeway).
5. `jti` a UUID; `instance_id` equal to the adapter's own registered instance ID.
6. `message_id`, `operation` (or `topic`) and `correlation_id` equal the values
   in the request.
7. `principal_kind`, `sub`, `roles`, `scope` and `actor` agree, by the same rules
   as the Gateway assertion.
8. The effective deadline is the smallest of the claim, the request context and
   the gRPC deadline.

Then the adapter builds the same caller identity that the HTTP middleware builds
today, and the existing handler checks (self, holder, scope) run unchanged. Any
failed check returns `UNAUTHENTICATED` with no detail, and the token is never logged.

## 5. Token issuance and bootstrap

- `user-management.issue_service_token` goes through the broker. The caller is
  authenticated by its certificate and `x-service-secret`; no bearer token is
  needed, so issuance never requires the token it issues.
- The broker requires the request's `service_name` to equal the certificate
  identity. It forwards the secret to User Management only as `x-service-secret`
  metadata on Execute. User Management checks the secret and its allowlist as it
  does today.
- Request and reply are VOLATILE: kept in memory with a bound, never written to
  the queue store, never logged. The typed request and reply mirror
  `ServiceTokenRequest` and `ServiceTokenResponse`, and are added with User
  Management's catalogue entries.
- Services keep tokens in memory, one per destination. They refresh at 80% of
  the lifetime, with a single refresh in flight per destination.
- Verified admin issuance through the Gateway stays as a VOLATILE public mapping.
  An invalid secret never falls back to admin.

Startup has no cycle: the CA, the certificates and the Gateway and broker public
keys are mounted files, and the broker reads UM's JWKS directly. Order: User
Management, broker, then the other services.

## 6. Keys and configuration

| Workload | Setting | Meaning |
| --- | --- | --- |
| message-broker | `BROKER_CONTEXT_PRIVATE_KEY_PATH`, `BROKER_CONTEXT_KEY_ID` | Signing key (RSA 2048+) and its `kid` |
| message-broker | `BROKER_CONTEXT_JWKS_PATH` | Public JWKS it serves and mounts for services |
| message-broker | `GATEWAY_ASSERTION_JWKS_PATH` | Gateway public keys |
| message-broker | `USER_MANAGEMENT_URL` | Where UM's JWKS is read |
| every service | `BROKER_CONTEXT_JWKS_PATH` | Broker public keys |
| every workload | `TLS_CA_PATH`, `TLS_CERT_PATH`, `TLS_KEY_PATH` | Section 1 |

The JWKS may hold two keys during rotation. Private keys are never committed and
never shared between workloads.

## 7. Required refusal tests

Each owner shows these with component doubles first, then in the live stack:

- wrong or missing client certificate, or a certificate for another workload
- Gateway assertion with the wrong issuer, type, audience or correlation ID, or expired
- service token with the wrong issuer, audience, subject or missing scope, or expired
- broker context with the wrong audience or instance, or expired, or with a changed
  operation or message ID, or repeated metadata
- issuance with the wrong secret, or with a `service_name` other than the certificate identity
- no token, secret or assertion in queue tables, replies or logs
