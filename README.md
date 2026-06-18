# BudgetyTzar

BudgetyTzar is a personal budgeting MVP that replaces a monthly spreadsheet with a PostgreSQL-backed HTTP API. The target domain model compares planned budget activity with actual transaction activity: budgets contain budget items, debit and credit budget adjustments, zero-sum reallocations, debit and credit transactions, transaction allocations, snapshots, and durable audit records. Phase 2 introduces Kafka-compatible local infrastructure, outbox publishing, and projection-backed snapshots/audit behind disabled-by-default feature flags.

## Phase 1 API

The current implementation is being migrated from an older period-based model toward the ledger-first API described in `SPECIFICATION.md`. The intended Phase 1 surface is:

- `POST /api/budgets`
- `GET /api/budgets`
- `GET /api/budgets/{budgetId}`
- `POST /api/budgets/{budgetId}/budget-items`
- `GET /api/budgets/{budgetId}/budget-items`
- `POST /api/budgets/{budgetId}/budget-items/{budgetItemId}/archive`
- `POST /api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments`
- `GET /api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments`
- `POST /api/budgets/{budgetId}/reallocations`
- `GET /api/budgets/{budgetId}/reallocations`
- `GET /api/budgets/{budgetId}/snapshot?date={date}`
- `POST /api/budgets/{budgetId}/transaction-imports/preview`
- `POST /api/budgets/{budgetId}/transaction-imports/{importBatchId}/commit`
- `GET /api/budgets/{budgetId}/transaction-imports/{importBatchId}`
- `POST /api/budgets/{budgetId}/transactions`
- `GET /api/budgets/{budgetId}/transactions?from={date}&to={date}&allocationStatus={status}`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}`
- `PUT /api/budgets/{budgetId}/transactions/{transactionId}`
- `POST /api/budgets/{budgetId}/transactions/{transactionId}/ignore`
- `PUT /api/budgets/{budgetId}/transactions/{transactionId}/allocations`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}/allocations`
- `DELETE /api/budgets/{budgetId}/transactions/{transactionId}/allocations`
- `GET /api/budgets/{budgetId}/audit-events`

Snapshot balances are planned-vs-actual positions: `actualCredits - plannedCredits + plannedDebits - actualDebits`. Budget item balances are cumulative by default. Unallocated debit and credit transaction values remain visible in snapshots until they are allocated to budget items.

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

The API remains runnable with only PostgreSQL. Kafka publishing, Kafka consuming, and projection-backed snapshots are disabled by default in `src/BudgetyTzar.Api/appsettings.json`.

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

Phase 1 is intentionally a local modular MVP. It keeps the language, domain model, audit vocabulary, and API surface aligned with the later event-driven architecture from `SPECIFICATION.md`, while deferring service decomposition, Kubernetes, and the Go implementation to later phases. Phase 2 starts introducing Kafka-compatible local infrastructure, domain-contract-shaped events, outbox publishing, and projection-backed snapshots/audit behind explicit opt-in flags.

Budgets are the root resource. Budget items are named buckets, not fixed debit or credit lines. Dated budget adjustments and transaction allocations can be debits or credits against any budget item, and item balances are cumulative planned-vs-actual positions. A budget has one currency, and all child amounts use that currency.

The Phase 1 audit timeline is backed by durable local audit records for imports, allocation changes, splits, ignores, reallocations, adjustments, and budget item archival. Kafka-published audit events, outbox records, and projection-backed reporting are Phase 2 concerns and should stay disabled in local config unless that behavior is being developed or tested.
