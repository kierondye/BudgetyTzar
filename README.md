# BudgetyTzar

BudgetyTzar is a personal budgeting MVP that replaces a monthly spreadsheet with a PostgreSQL-backed HTTP API. Phase 1 focuses on the local .NET implementation: budget periods, categories, income sources, manual transactions, transaction assignment, budget movements, and a monthly dashboard projection.

## Phase 1 API

- `POST /api/budget-categories`
- `GET /api/budget-categories`
- `POST /api/income-sources`
- `GET /api/income-sources`
- `POST /api/budget-periods`
- `GET /api/budget-periods`
- `POST /api/budget-periods/{id}/allocations`
- `POST /api/budget-periods/{id}/expected-income`
- `POST /api/budget-periods/{id}/movements`
- `POST /api/transactions`
- `GET /api/transactions?budgetPeriodId={id}`
- `GET /api/transactions/{id}`
- `POST /api/transactions/{id}/assign`
- `POST /api/transactions/{id}/ignore`
- `GET /api/reports/monthly-summary?budgetPeriodId={id}`
- `GET /api/reports/audit-timeline?budgetPeriodId={id}`

## Local Development

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Run the API:

```bash
dotnet run --project src/BudgetyTzar.Api
```

The API creates the local schema on startup when `Database:EnsureCreatedOnStartup` is `true`. For production-style deployments this should be replaced with EF Core migrations.

Run tests:

```bash
dotnet test
```

## Architecture Notes

Phase 1 is intentionally a local modular MVP. It keeps the language, domain model, and API surface aligned with the later event-driven service architecture from `SPECIFICATION.md`, while deferring Kafka, outbox publishing, containerised services, Kubernetes, and the Go implementation to later phases.
