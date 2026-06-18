# BudgetyTzar

BudgetyTzar is a personal budgeting MVP that replaces a monthly spreadsheet with a PostgreSQL-backed HTTP API. Phase 1 focuses on the local .NET implementation: budgets, budget periods, budget lines, durable audit records, manual and imported transactions, transaction assignment, budget reallocations, adjustments, reconciliation, basic multi-period reports, and CSV export. Phase 2 introduces Kafka-compatible local infrastructure, outbox publishing, and projection-backed reporting behind disabled-by-default feature flags.

## Phase 1 API

- `POST /api/budgets`
- `GET /api/budgets`
- `GET /api/budgets/{budgetId}`
- `POST /api/budgets/{budgetId}/periods`
- `GET /api/budgets/{budgetId}/periods`
- `GET /api/budgets/{budgetId}/periods/{periodId}`
- `GET /api/budgets/{budgetId}/periods/for-date?date={date}`
- `POST /api/budgets/{budgetId}/budget-lines`
- `GET /api/budgets/{budgetId}/budget-lines`
- `POST /api/budgets/{budgetId}/budget-lines/{lineId}/archive`
- `PUT /api/budgets/{budgetId}/periods/{periodId}/allocations`
- `GET /api/budgets/{budgetId}/periods/{periodId}/allocations`
- `POST /api/budgets/{budgetId}/transaction-imports/preview`
- `POST /api/budgets/{budgetId}/transaction-imports/{importBatchId}/commit`
- `GET /api/budgets/{budgetId}/transaction-imports/{importBatchId}`
- `POST /api/budgets/{budgetId}/transactions`
- `GET /api/budgets/{budgetId}/transactions?periodId={periodId}`
- `GET /api/budgets/{budgetId}/transactions?from={date}&to={date}&assignmentStatus={status}`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}`
- `PUT /api/budgets/{budgetId}/transactions/{transactionId}`
- `POST /api/budgets/{budgetId}/transactions/{transactionId}/ignore`
- `PUT /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `DELETE /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `POST /api/budgets/{budgetId}/periods/{periodId}/reallocations`
- `GET /api/budgets/{budgetId}/periods/{periodId}/reallocations`
- `POST /api/budgets/{budgetId}/periods/{periodId}/adjustments`
- `GET /api/budgets/{budgetId}/periods/{periodId}/adjustments`
- `GET /api/budgets/{budgetId}/reports/period-summary?periodId={periodId}`
- `GET /api/budgets/{budgetId}/reports/budget-line-trends?budgetLineId={lineId}&from={date}&to={date}`
- `GET /api/budgets/{budgetId}/reports/credit-variance?from={date}&to={date}`
- `GET /api/budgets/{budgetId}/reports/reconciliation?periodId={periodId}`
- `GET /api/budgets/{budgetId}/reports/audit-timeline?periodId={periodId}`
- `GET /api/budgets/{budgetId}/reports/period-summary.csv?periodId={periodId}`

## Local Development

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Start the optional Phase 2 local event infrastructure:

```bash
docker compose up -d redpanda kafka-ui
```

Kafka UI is available at `http://localhost:8080/`. The local Kafka-compatible broker is Redpanda:

- API or host tools: `localhost:19092`
- Other Compose services: `redpanda:9092`
- Redpanda admin API: `http://localhost:9644`

To start all local infrastructure:

```bash
docker compose up -d
```

If you are moving from the old local `EnsureCreated` schema, reset the disposable
PostgreSQL volume first:

```bash
docker compose down -v
docker compose up -d postgres
```

Run the API:

```bash
dotnet run --project src/BudgetyTzar.Api
```

Open `http://localhost:5000/` or `http://localhost:5000/swagger` to browse the API. The Swagger JSON is available at `http://localhost:5000/swagger/v1/swagger.json`, and the health check is available at `http://localhost:5000/health`.

The API applies EF Core migrations on startup when `Database:MigrateOnStartup` is `true`.

### Phase 2 Kafka, Outbox, and Projection Flags

The API remains runnable with only PostgreSQL. Kafka publishing, Kafka consuming, and projection-backed reports are disabled by default in `src/BudgetyTzar.Api/appsettings.json`.

Default local settings:

- `Kafka:BootstrapServers`: `localhost:19092`
- `Kafka:Topics:BudgetingEvents`: `budgetytzar.budgeting.events`
- `Kafka:Topics:TransactionEvents`: `budgetytzar.transactions.events`
- `Kafka:Topics:ReportingEvents`: `budgetytzar.reporting.events`
- `Outbox:PublisherEnabled`: `false`
- `Projections:ConsumerEnabled`: `false`
- `Projections:UseProjectionBackedReports`: `false`

Opt in locally with environment variables when working on Phase 2 behavior:

```bash
Outbox__PublisherEnabled=true \
Projections__ConsumerEnabled=true \
Projections__UseProjectionBackedReports=true \
dotnet run --project src/BudgetyTzar.Api
```

Only enable these flags after starting `redpanda`. Leave them disabled for normal API work that does not need Kafka.

Run tests:

```bash
dotnet test
```

## Architecture Notes

Phase 1 is intentionally a local modular MVP. It keeps the language, domain model, audit vocabulary, and API surface aligned with the later event-driven service architecture from `SPECIFICATION.md`, while deferring service decomposition, Kubernetes, and the Go implementation to later phases. Phase 2 starts introducing Kafka-compatible local infrastructure, outbox publishing, and projection-backed reporting behind explicit opt-in flags.

Budgets are the root resource. Budget periods cannot overlap within a budget, transactions belong to a budget, and a transaction's date determines which period reports include it in. Budget lines can be debit or credit lines, and debit lines can either reset each period or carry cumulative balances forward. Archived budget lines remain visible in historical periods. A budget has one currency, and all child amounts use that currency.

The Phase 1 audit timeline is backed by durable local audit records for imports, assignment changes, splits, ignores, reallocations, adjustments, and budget line archival. Kafka-published audit events, outbox records, and projection-backed reporting are Phase 2 concerns and should stay disabled in local config unless that behavior is being developed or tested.
