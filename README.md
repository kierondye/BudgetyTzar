# BudgetyTzar

BudgetyTzar is a personal budgeting MVP that replaces a monthly spreadsheet with a PostgreSQL-backed HTTP API. The target domain model compares planned budget activity with actual transaction activity: budgets contain budget items, debit and credit budget adjustments, zero-sum reallocations, debit and credit transactions, transaction allocations, snapshots, and durable audit records. Phase 2 introduces Kafka-compatible local infrastructure, outbox publishing, and projection-backed snapshots/audit as the default local event-driven path.

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

Start the local infrastructure:

```bash
docker compose up -d
```

For database-only work, you can start PostgreSQL on its own and disable the Kafka-backed workers with environment variables when running the API:

```bash
docker compose up -d postgres
Outbox__PublisherEnabled=false \
Projections__ConsumerEnabled=false \
Projections__UseProjectionBackedReports=false \
dotnet run --project src/BudgetyTzar.Api
```

Kafka UI is available at `http://localhost:8080/`. The local Kafka-compatible broker is Redpanda:

- API or host tools: `localhost:19092`
- Other Compose services: `redpanda:9092`
- Redpanda admin API: `http://localhost:9644`

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

Open `http://localhost:7070/` or `http://localhost:7070/swagger` to browse the API. The Swagger JSON is available at `http://localhost:7070/swagger/v1/swagger.json`, and the health check is available at `http://localhost:7070/health`.

The API applies EF Core migrations on startup when `Database:MigrateOnStartup` is `true`.

Runtime version metadata is available at `http://localhost:7070/version`. BudgetyTzar uses one product-wide SemVer derived from Git tags and Conventional Commits. Tagged commits expose the tag version, such as `0.1.0`; commits after a tag expose deterministic preview metadata, such as `0.2.0-preview.3+abc1234`.

Conventional Commits are required from Phase 2.5 onward. Enable the versioned local hook once per clone:

```bash
git config core.hooksPath .githooks
```

Use commit subjects such as `feat: add budget export`, `fix(api): preserve version endpoint metadata`, or `feat(events)!: rename transaction event`.

Cut a local release tag intentionally with:

```bash
scripts/release.sh
```

Git tags are the canonical released versions. Human release notes live in GitHub Releases; the release script prints the matching `gh release create` command when the GitHub CLI is available.

### Phase 2 Kafka, Outbox, and Projection Flags

Kafka publishing, Kafka consuming, and projection-backed snapshots are enabled by default in `src/BudgetyTzar.Api/appsettings.json`.

Default local settings:

- `Kafka:BootstrapServers`: `localhost:19092`
- `Kafka:Topics:BudgetingEvents`: `budgetytzar.budgeting.events`
- `Kafka:Topics:TransactionEvents`: `budgetytzar.transactions.events`
- `Kafka:Topics:ReportingEvents`: `budgetytzar.reporting.events`
- `Outbox:PublisherEnabled`: `true`
- `Projections:ConsumerEnabled`: `true`
- `Projections:UseProjectionBackedReports`: `true`

Opt out locally with environment variables when you want to run the API without Kafka:

```bash
Outbox__PublisherEnabled=false \
Projections__ConsumerEnabled=false \
Projections__UseProjectionBackedReports=false \
dotnet run --project src/BudgetyTzar.Api
```

Start `redpanda` before using the default settings.

Run tests:

```bash
dotnet test
```

## Architecture Notes

Phase 1 is intentionally a local modular MVP. It keeps the language, domain model, audit vocabulary, and API surface aligned with the later event-driven architecture from `SPECIFICATION.md`, while deferring service decomposition, Kubernetes, and the Go implementation to later phases. Phase 2 starts introducing Kafka-compatible local infrastructure, domain-contract-shaped events, outbox publishing, and projection-backed snapshots/audit as the default local path.

Budgets are the root resource. Budget items are named buckets, not fixed debit or credit lines. Dated budget adjustments and transaction allocations can be debits or credits against any budget item, and item balances are cumulative planned-vs-actual positions. A budget has one currency, and all child amounts use that currency.

The Phase 1 audit timeline is backed by durable local audit records for imports, allocation changes, ignores, reallocations, adjustments, and budget item archival. Kafka-published audit events, outbox records, and projection-backed reporting are Phase 2 concerns and are enabled in local config by default.
