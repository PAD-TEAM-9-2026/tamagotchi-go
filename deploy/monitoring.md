# Monitoring

Prometheus scrapes every service replica and Grafana shows four provisioned dashboards. Neither
is part of `compose.yaml` yet: the service definitions below are what the Compose owner adds.

| Path | Content |
| --- | --- |
| `prometheus/prometheus.yml` | Scrape every 15 s, one job per service, replicas found by DNS A records on the service name |
| `grafana/provisioning/datasources/prometheus.yml` | The `Prometheus` datasource at `http://prometheus:9090`, not editable |
| `grafana/provisioning/dashboards/dashboards.yml` | Loads `grafana/dashboards/` into the folder `Tamagotchi Go` |
| `grafana/dashboards/*.json` | Overview, Instances, Events, Notification |

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
job in `prometheus.yml` and change `job_name` and `names`. The panels need only the common names:
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

A visible push on a device is not measurable from the server, so the Notification dashboard shows
provider acceptance only. The broker's own dashboard is added with its metrics.
