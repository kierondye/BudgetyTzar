# BudgetyTzar

BudgetyTzar is a personal budgeting MVP that replaces a monthly spreadsheet with a PostgreSQL-backed HTTP API. Phase 1 focuses on the local .NET implementation: budgets, budget periods, budget lines, manual transactions, transaction assignment, budget reallocations, and a period summary projection.

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
- `POST /api/budgets/{budgetId}/transactions`
- `GET /api/budgets/{budgetId}/transactions?periodId={periodId}`
- `GET /api/budgets/{budgetId}/transactions?from={date}&to={date}&assignmentStatus={status}`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}`
- `POST /api/budgets/{budgetId}/transactions/{transactionId}/ignore`
- `PUT /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `GET /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `DELETE /api/budgets/{budgetId}/transactions/{transactionId}/assignments`
- `POST /api/budgets/{budgetId}/periods/{periodId}/reallocations`
- `GET /api/budgets/{budgetId}/periods/{periodId}/reallocations`
- `GET /api/budgets/{budgetId}/reports/period-summary?periodId={periodId}`
- `GET /api/budgets/{budgetId}/reports/audit-timeline?periodId={periodId}`

## Local Development

Start PostgreSQL:

```bash
docker compose up -d postgres
```

Run the API:

```bash
dotnet run --project src/BudgetyTzar.Api
```

Open `http://localhost:5000/` or `http://localhost:5000/swagger` to browse the API. The Swagger JSON is available at `http://localhost:5000/swagger/v1/swagger.json`, and the health check is available at `http://localhost:5000/health`.

The API creates the local schema on startup when `Database:EnsureCreatedOnStartup` is `true`. For production-style deployments this should be replaced with EF Core migrations.

Run tests:

```bash
dotnet test
```

## Architecture Notes

Phase 1 is intentionally a local modular MVP. It keeps the language, domain model, and API surface aligned with the later event-driven service architecture from `SPECIFICATION.md`, while deferring Kafka, outbox publishing, containerised services, Kubernetes, and the Go implementation to later phases.

Budgets are the root resource. Budget periods cannot overlap within a budget, transactions belong to a budget, and a transaction's date determines which period reports include it in. Budget lines can be debit or credit lines, and debit lines can either reset each period or carry cumulative balances forward. A budget has one currency, and all child amounts use that currency.
