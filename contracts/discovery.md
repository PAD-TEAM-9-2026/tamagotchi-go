# Discovery Registration Format

This document records the registration shape accepted by Service Discovery
(merged in service-discovery #60). All registering services must conform to it.

## Registration body: `POST /v1/instances`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `instance_id` | UUID string | yes | Stable per-process ID; regenerated on each start |
| `service_name` | string `^[a-z][a-z0-9-]{1,62}$` | yes | Canonical logical service name (e.g. `guild`) |
| `host` | string 1–253 chars | yes | Hostname or IPv4 address (no scheme) |
| `port` | integer 1–65535 | yes | HTTP port |
| `status_path` | string | no | Health-check path; defaults to `/status` |
| `version` | string or null | no | Semver image version |
| `grpc_endpoint` | `host:port` string or null | no | gRPC `ServiceDelivery` listener; TLS required at the endpoint |
| `subscriptions` | array of Subscription (max 32) | no | Topics this instance consumes; the Broker authorizes them against the operations manifest |

## Subscription object

| Field | Type | Description |
| --- | --- | --- |
| `topic` | string | Event topic matching `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*\.v[1-9][0-9]*$` (e.g. `user.package_joined.v1`) |
| `mode` | `"logical"` or `"instance"` | `"logical"`: competing consumers across replicas (normal services); `"instance"`: bounded expiring per-replica queue (Guild chat relay only) |

## Instance response: `GET /v1/services/{name}/instances`

The same fields are returned, plus:

| Field | Type | Description |
| --- | --- | --- |
| `base_url` | string | `http://{host}:{port}` |
| `health` | `"unknown"` / `"healthy"` / `"unhealthy"` | Most recent health-check result |
| `last_load` | object or null | Load snapshot from the last status response (see below) |
| `registered_at` | ISO-8601 timestamp | |
| `last_checked_at` | ISO-8601 timestamp or null | Time of the most recent health poll |

### Load object fields (from `/status` response body)

Routing components use `in_flight_requests` to select the least-loaded routable
instance. All fields are optional; absent fields are treated as zero.

| Field | Type | Description |
| --- | --- | --- |
| `in_flight_requests` | integer | Active concurrent requests being processed |
| `requests_last_minute` | integer | Requests completed in the last 60 seconds |
| `avg_response_ms_last_minute` | number | Mean response latency over the last 60 seconds |

## Routing rules

- Only instances with `health != "unhealthy"` are returned when `?routable=true`.
- `grpc_endpoint` is `host:port` with **no scheme**. The caller is responsible for
  establishing a mTLS connection with SPIFFE peer validation.
- Each `topic` may appear at most once per registration.
- `"instance"` mode subscriptions are exclusively for Guild chat relay; all other
  subscribers must use `"logical"`.
- Offline subscriber queues persist across deregistration and restart; the broker
  resumes delivery when the service re-registers.
