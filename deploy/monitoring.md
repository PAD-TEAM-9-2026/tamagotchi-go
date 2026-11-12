# Monitoring

Prometheus scrapes every service replica and the message broker, and Grafana shows five provisioned dashboards. Use the optional `compose.monitoring.yaml` overlay with the reviewed candidate base.
Scrape configuration is provisioned for the target services; a job is accepted only
when its actual candidate exports metrics. Missing targets are not healthy replicas.

| Path | Content |
| --- | --- |
| `prometheus/prometheus.yml` | Scrape every 15 s, one job per service, replicas found by DNS A records on the service name |
| `grafana/provisioning/datasources/prometheus.yml` | The `Prometheus` datasource at `http://prometheus:9090`, not editable |
| `grafana/provisioning/dashboards/dashboards.yml` | Loads `grafana/dashboards/` into the folder `Tamagotchi Go` |
| `grafana/dashboards/*.json` | Overview, Instances, Events, Notification, Message broker |

## Compose

```sh
docker compose --env-file /path/to/private/candidate.env \
  -f compose.yaml -f compose.broker.yaml -f compose.monitoring.yaml config --quiet
docker compose --env-file /path/to/private/candidate.env \
  -f compose.yaml -f compose.broker.yaml -f compose.monitoring.yaml up -d prometheus grafana
```

Set `GRAFANA_ADMIN_PASSWORD` in the private environment. Grafana binds to loopback
port3030 by default (`GRAFANA_PORT` overrides it); Prometheus publishes no host
port. Their named volumes preserve dashboards/state and seven days of bounded
metrics. This overlay does not switch domain transports or publish images.

## Adding a service

Every service exports the common families on `METRICS_PORT` (default 9090). To scrape it, copy a
job in `prometheus.yml` and change `job_name` and `names`. The broker's job is `message-broker`, found by the DNS name of its Compose service. The panels need only the common names:
`http_requests_total`, `http_request_duration_seconds`, `tasks_in_flight`, `tasks_capacity`,
`task_rejections_total`, `deliveries_total`, `build_info` and `log_file_write_errors_total`.
Label values stay bounded: route templates, operation names, topics and fixed outcomes, never an
identifier.

## Dashboards

| Dashboard | Shows |
| --- | --- |
| Overview | Instances up, request rate, 5xx share, p95 latency, status codes, task rejections, versions |
| Instances | Tasks in flight against capacity per replica, restarts, memory, failed log writes |
| Events | Deliveries by outcome and topic, outbox backlog and age, publishes, background runs, credits |
| Notification | Stored notifications, provider acceptance and failures, push breaker, redeliveries |
| Message broker | Requests by outcome and operation, call latency, pool use, queue depth and oldest age, dead letters, delivery attempts, retries and deferrals, route distribution, circuit breaker per instance, log exports |

A visible push on a device is not measurable from the server, so the Notification dashboard shows
provider acceptance only.

The broker does not export the common HTTP families. Its dashboard uses `broker_requests_total`,
`broker_request_duration_seconds`, `broker_queue_depth`, `broker_queue_oldest_age_seconds`,
`broker_dead_letters`, `broker_delivery_attempts_total`, `broker_delivery_retries_total`,
`broker_delivery_deferrals_total`, `broker_route_attempts_total`, `broker_breaker_state`,
`tasks_in_flight`, `tasks_capacity` and the four `log_export` series, all described in the broker
README. Queue and dead-letter values come from the broker's one database, so the panels take the
maximum over replicas. `target` is an instance ID.

The optional [structured log volume overlay](log-volumes.md) gives applications
service-owned log storage and Broker read-only access for system log export.
It is separate from metrics scraping and requires the Broker candidate base.
