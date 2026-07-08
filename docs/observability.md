# Observability

BudgetyTzar emits structured request logs, OpenTelemetry traces, and metrics without
requiring a telemetry collector for API startup or automated tests. Exporters are
disabled by default.

## Correlation and tracing

Every response includes `X-Correlation-ID`. A caller-supplied value is propagated only
when there is exactly one header value and it is 8–64 characters long, starts with an
ASCII letter or digit, and otherwise contains only ASCII letters, digits, `.`, `_`, or
`-`. The API replaces missing or invalid values with a 32-character lowercase GUID.

The correlation ID is attached to the server activity as `correlation.id` and to the
structured log scope as `CorrelationId`. The scope also includes the W3C `TraceId`, so
logs and traces can be joined. Normal W3C `traceparent` propagation is handled by the
ASP.NET Core and `HttpClient` OpenTelemetry instrumentation.

Request logs and custom signals never include request or response bodies. Raw server
paths and query strings are removed from exported spans because they may contain
resource identifiers or financial search data. Stable route templates are used
instead. Transaction descriptions, monetary amounts, resource IDs, and authenticated
subject identifiers must not be added to log properties, span attributes, metric
dimensions, or correlation IDs.

## Signals

All custom metrics come from the `BudgetyTzar.Api` meter.

| Signal | Type | Low-cardinality dimensions |
| --- | --- | --- |
| `budgetytzar.api.requests` | Counter | `http.request.method`, `http.route`, `http.response.status_code` |
| `budgetytzar.api.errors` | Counter | `http.request.method`, `http.route`, `http.response.status_code` |
| `budgetytzar.api.request.duration` | Histogram (seconds) | `http.request.method`, `http.route`, `http.response.status_code` |
| `budgetytzar.validation.failures` | Counter | `http.route` |
| `budgetytzar.transaction.allocation.failures` | Counter | `failure.reason` |
| `budgetytzar.budget_summary.duration` | Histogram (seconds) | `query.outcome` |

Allocation failure reasons are the stable values `transaction_not_found`,
`budget_item_not_found`, `currency_mismatch`, and `already_allocated`. Budget Summary
outcomes are `found`, `not_found`, and `error`.

The OpenTelemetry ASP.NET Core and `HttpClient` integrations also provide standard
server and outbound-client spans and metrics. Database instrumentation can be added
when a database provider is introduced; this increment does not add database-specific
hooks to the in-memory repositories.

## Exporters and local verification

To print telemetry while developing:

```bash
OpenTelemetry__ConsoleExporter__Enabled=true \
  dotnet run --project src/BudgetyTzar.Api
```

For an OTLP-compatible collector or backend:

```bash
OpenTelemetry__OtlpExporter__Enabled=true \
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  dotnet run --project src/BudgetyTzar.Api
```

Standard `OTEL_EXPORTER_OTLP_*` variables configure protocol, headers, timeouts, and
per-signal endpoints. An unavailable exporter may drop telemetry but does not change
API responses. Leave both exporters disabled for ordinary tests.

Send a request and inspect its returned correlation value:

```bash
curl -i -H 'X-Correlation-ID: local-check-123' http://localhost:7070/api/version
```
