# Monitoring

Prometheus scrapes every service replica and the message broker, and Grafana shows five provisioned dashboards. Jaeger
collects traces ([Tracing](#tracing)). None of them is part of `compose.yaml` yet: the service definitions below are what
the Compose owner adds.

| Path | Content |
| --- | --- |
| `prometheus/prometheus.yml` | Scrape every 15 s, one job per service, replicas found by DNS A records on the service name |
| `grafana/provisioning/datasources/prometheus.yml` | The `Prometheus` datasource at `http://prometheus:9090`, not editable |
| `grafana/provisioning/dashboards/dashboards.yml` | Loads `grafana/dashboards/` into the folder `Tamagotchi Go` |
| `grafana/dashboards/*.json` | Overview, Instances, Events, Notification, Message broker |

## Compose

```yaml
  prometheus:
    image: prom/prometheus:v3.5.0
    command:
      - --config.file=/etc/prometheus/prometheus.yml
      - --storage.tsdb.path=/prometheus
      - --storage.tsdb.retention.time=7d
      - --storage.tsdb.retention.size=2GB
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - shared-prometheus-data:/prometheus

  grafana:
    image: grafana/grafana:12.2.0
    depends_on:
      - prometheus
    environment:
      GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_ADMIN_PASSWORD:?set GRAFANA_ADMIN_PASSWORD in .env}
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./grafana/provisioning:/etc/grafana/provisioning:ro
      - ./grafana/dashboards:/var/lib/grafana/dashboards:ro
      - shared-grafana-data:/var/lib/grafana
    ports:
      - "127.0.0.1:3030:3000"
```

Add `shared-prometheus-data` and `shared-grafana-data` under `volumes`. Prometheus publishes no
port, and the services' metrics port 9090 is reachable only on the Compose network.

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

## Tracing

Jaeger shows OpenTelemetry traces: one request followed through every service it touches. It does not
replace logs or metrics. Each service sends spans over OTLP/HTTP to `http://jaeger:4318` once it sets the
variables below; without `OTEL_EXPORTER_OTLP_ENDPOINT` a service starts no tracing at all. Every log line
written inside a span carries `trace_id` and `span_id`, so a line from the log export can be looked up in
Jaeger by trace ID.

| Path | Content |
| --- | --- |
| `jaeger/config.yaml` | OTLP receivers on 4317 (gRPC) and 4318 (HTTP), memory storage capped at 50,000 traces, and a processor that deletes query strings, URL paths, SQL text, headers and gRPC metadata from spans |
| `grafana/provisioning/datasources/jaeger.yml` | The `Jaeger` datasource at `http://jaeger:16686`, not editable |

```yaml
  jaeger:
    image: jaegertracing/jaeger:2.23.0
    command: ["--config", "/etc/jaeger/config.yaml"]
    volumes:
      - ./jaeger/config.yaml:/etc/jaeger/config.yaml:ro
    ports:
      - "127.0.0.1:16686:16686"
    mem_limit: 768m
```

The UI is on `127.0.0.1:16686` only; OTLP is reachable only on the Compose network. The message-broker
candidate (`compose.broker.yaml`) already contains this service. Each traced service gets:

```yaml
      OTEL_EXPORTER_OTLP_ENDPOINT: http://jaeger:4318
      OTEL_EXPORTER_OTLP_PROTOCOL: http/protobuf
      OTEL_METRICS_EXPORTER: none
      OTEL_LOGS_EXPORTER: none
```

`service.name`, `service.version` and `service.instance.id` (the Discovery instance ID) are set by the
service itself. Probes, metrics endpoints and calls to Service Discovery are not traced, and spans never
hold tokens, headers, bodies, query strings, SQL text or user IDs.

The optional [structured log volume overlay](log-volumes.md) gives applications
service-owned log storage and Broker read-only access for system log export.
It is separate from metrics scraping and requires the Broker candidate base.
