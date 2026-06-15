# BudgetyTzar Application Specification

## 1. Purpose

BudgetyTzar is a personal budgeting application that replaces a manual monthly spreadsheet process with an auditable, event-driven system. It tracks expected income, actual income, budget allocations, spending, credits, transfers between budget lines, and cumulative savings-style categories across multiple months.

The application is also intended to demonstrate senior software engineering capability through two equivalent implementations:

- A .NET implementation.
- A Go implementation.

Both implementations should use the same product model, event contracts, test scenarios, container strategy, Kubernetes deployment model, and cloud architecture.

## 2. Goals

### 2.1 Product Goals

- Reduce manual budgeting effort.
- Preserve transaction-level detail instead of flattening everything into a single monthly spent column.
- Clearly distinguish income, spending, refunds, budget allocations, and budget movements.
- Support budget categories that reset monthly and categories that accumulate over time.
- Track expected earnings against actual earnings.
- Provide confidence that the budget is correct through audit trails and reconciliation views.
- Support analysis across many months, not just one sheet at a time.

### 2.2 Engineering Goals

- Demonstrate event-driven architecture using Kafka.
- Demonstrate containerised services.
- Demonstrate Kubernetes deployment.
- Demonstrate cloud-readiness.
- Demonstrate equivalent service design in .NET and Go.
- Demonstrate clean domain modelling, testing, observability, and operational thinking.

## 3. Non-Goals

- Direct bank integration is not required for the first version.
- Real-time Open Banking synchronisation is not required for the first version.
- Multi-user household budgeting is not required for the first version.
- Mobile applications are not required for the first version.
- Investment portfolio tracking is not required.

## 4. Target User

The primary user is an individual who budgets monthly, reviews bank transactions, allocates those transactions to budget lines, adjusts budget during the month, and wants clear historical analysis.

The secondary audience is prospective employers reviewing the project as evidence of engineering capability.

## 5. Core Concepts

### 5.1 Budget Period

A budget period usually represents one calendar month.

Examples:

- January 2026
- February 2026

A period has:

- Start date.
- End date.
- Opening balances for cumulative categories.
- Planned income.
- Planned category allocations.
- Actual income.
- Actual spending.
- Closing balances.

### 5.2 Budget Category

A budget category represents a line in the budget.

Examples:

- Mortgage
- Groceries
- Petrol
- Eating out
- Holiday fund
- Car maintenance
- Christmas

Categories have a type:

- `MonthlyReset`: unused budget does not carry forward automatically.
- `Cumulative`: unused budget carries forward into future periods.

Categories may be active or archived.

### 5.3 Income Source

An income source represents expected money coming in.

Examples:

- Salary
- Bonus
- Reimbursement
- Interest

Income sources may be recurring or one-off.

### 5.4 Transaction

A transaction is an imported or manually entered financial movement.

Transactions preserve:

- Date.
- Description.
- Amount.
- Direction: debit or credit.
- Source account.
- External reference, if available.
- Import batch, if imported.
- Assigned category or income source.
- Notes.

### 5.5 Allocation

An allocation assigns available income or carried-forward balance to a budget category for a budget period.

Example:

- Allocate 400.00 to Groceries for June 2026.

### 5.6 Budget Movement

A budget movement transfers available budget from one category to another without representing a bank transaction.

Example:

- Move 50.00 from Eating out to Groceries.

Budget movements must be stored separately from real transactions.

### 5.7 Adjustment

An adjustment corrects or explains a balance without pretending it was a bank transaction.

Examples:

- Opening balance correction.
- Manual reconciliation adjustment.
- Write-off.

Adjustments should require a reason.

## 6. Functional Requirements

### 6.1 Budget Setup

The user can:

- Create budget categories.
- Mark categories as monthly reset or cumulative.
- Archive categories that are no longer used.
- Create income sources.
- Configure expected recurring income.
- Configure expected recurring category allocations.
- Start a new budget period from a template or previous period.

Acceptance criteria:

- A new month can be created with all expected income and budget categories pre-populated.
- Cumulative categories start with their previous closing balance.
- Monthly reset categories start from zero unless explicitly allocated.

### 6.2 Transaction Entry and Import

The user can:

- Manually add transactions.
- Import transactions from a CSV file.
- View imported transactions before committing them.
- Detect likely duplicate transactions.
- Assign transactions to budget categories or income sources.
- Split a single transaction across multiple categories.
- Mark transactions as ignored when they are not relevant to the budget.

Acceptance criteria:

- Imported transaction details remain visible after assignment.
- Debit transactions reduce available category budget.
- Credit transactions assigned to an income source increase actual income.
- Credit transactions assigned to a spending category behave as refunds or reimbursements.

### 6.3 Budget Tracking

The user can:

- View planned income vs actual income for a period.
- View planned spending vs actual spending by category.
- View current remaining budget by category.
- See over-budget categories.
- Move budget between categories.
- See a clear distinction between real spending, refunds, allocations, transfers, and adjustments.

Acceptance criteria:

- Overspending is visible without needing to inspect formulas.
- Moving budget between categories does not alter actual spending totals.
- Category balances can be recalculated from event history.

### 6.4 Cumulative Categories

The user can:

- Carry unused budget forward for cumulative categories.
- Spend from accumulated balances.
- View opening balance, monthly allocation, spending, transfers, adjustments, and closing balance.

Acceptance criteria:

- A cumulative category's closing balance becomes the next period's opening balance.
- Monthly reset category balances do not carry forward by default.

### 6.5 Income Tracking

The user can:

- Define expected income for a period.
- Assign credit transactions to income sources.
- Compare expected income with actual income.
- See income variance by source and by month.

Acceptance criteria:

- The system shows whether actual earnings matched expected earnings.
- Income is not mixed into spending totals.

### 6.6 Audit Trail

The user can:

- View the history of changes affecting a budget period.
- See when transactions were imported, assigned, split, edited, or ignored.
- See when budget was moved between categories.
- See why adjustments were made.

Acceptance criteria:

- The current budget state can be explained from recorded events.
- The user can understand an old month without relying on spreadsheet context.

### 6.7 Reporting and Analysis

The user can:

- Compare spending across months.
- View trends by category.
- View income trends.
- View budget variance over time.
- Export data to CSV.

Acceptance criteria:

- The user can answer: "How much did I spend on groceries over the last 12 months?"
- The user can answer: "Which categories am I consistently overspending?"
- The user can answer: "Did I earn what I expected over the last year?"

## 7. Example User Journeys

### 7.1 Start a New Month

1. User selects "Create Budget Period".
2. User chooses July 2026.
3. System copies recurring income and allocation templates.
4. System carries forward balances for cumulative categories.
5. User reviews and confirms the new period.

### 7.2 Import and Assign Transactions

1. User uploads a bank CSV.
2. System parses and previews transactions.
3. System flags possible duplicates.
4. User commits the import.
5. User assigns each transaction to a category or income source.
6. System updates projected budget balances.

### 7.3 Cover Overspending

1. User sees Groceries is 30.00 over budget.
2. User moves 30.00 from Eating out to Groceries.
3. System records a budget movement.
4. Groceries is no longer over budget.
5. Actual spending remains unchanged.

## 8. Domain Events

The system should use domain events as the source of integration between services. Event names should be stable and versioned.

### 8.1 Event Naming

Recommended format:

```text
budgetytzar.<bounded-context>.<event-name>.v1
```

Examples:

- `budgetytzar.budgeting.budget-period-created.v1`
- `budgetytzar.transactions.transaction-imported.v1`
- `budgetytzar.budgeting.budget-movement-recorded.v1`

### 8.2 Core Events

Budgeting events:

- `BudgetPeriodCreated`
- `BudgetCategoryCreated`
- `BudgetCategoryArchived`
- `BudgetCategoryTypeChanged`
- `CategoryAllocationPlanned`
- `CategoryAllocationChanged`
- `BudgetMovementRecorded`
- `BudgetAdjustmentRecorded`
- `BudgetPeriodClosed`
- `BudgetPeriodReopened`

Income events:

- `IncomeSourceCreated`
- `IncomeExpected`
- `IncomeExpectationChanged`
- `IncomeReceived`
- `IncomeTransactionAssigned`

Transaction events:

- `TransactionImportBatchCreated`
- `TransactionImported`
- `TransactionManuallyCreated`
- `TransactionDuplicateDetected`
- `TransactionAssigned`
- `TransactionSplit`
- `TransactionIgnored`
- `TransactionEdited`

Reporting events:

- `BudgetProjectionUpdated`
- `MonthlySummaryCalculated`
- `CategoryTrendCalculated`

### 8.3 Event Envelope

All events should share a common envelope:

```json
{
  "eventId": "uuid",
  "eventType": "budgetytzar.transactions.transaction-imported.v1",
  "occurredAt": "2026-06-15T10:30:00Z",
  "correlationId": "uuid",
  "causationId": "uuid",
  "aggregateId": "uuid",
  "aggregateType": "Transaction",
  "schemaVersion": 1,
  "payload": {}
}
```

### 8.4 Example Event Payloads

#### TransactionImported

```json
{
  "transactionId": "uuid",
  "importBatchId": "uuid",
  "accountId": "uuid",
  "transactionDate": "2026-06-12",
  "description": "SUPERMARKET",
  "amount": "42.30",
  "currency": "GBP",
  "direction": "Debit",
  "externalReference": "bank-reference"
}
```

#### BudgetMovementRecorded

```json
{
  "budgetMovementId": "uuid",
  "budgetPeriodId": "uuid",
  "fromCategoryId": "uuid",
  "toCategoryId": "uuid",
  "amount": "30.00",
  "currency": "GBP",
  "reason": "Cover grocery overspend"
}
```

## 9. Bounded Contexts and Services

The application should be decomposed into small services for demonstration purposes. A simpler modular-monolith implementation may also be useful locally, but the portfolio architecture should show service boundaries.

### 9.1 Identity Service

Responsibilities:

- User authentication.
- User profile.
- Tenant ownership, even if only one user exists initially.

Suggested implementation:

- Use an external identity provider with OpenID Connect.
- Both the .NET and Go implementations should validate tokens issued by the same provider.
- The application should not implement its own password storage or identity management.

### 9.2 Budgeting Service

Responsibilities:

- Budget periods.
- Categories.
- Planned allocations.
- Budget movements.
- Adjustments.
- Period closing and reopening.

Owns:

- Budget periods.
- Budget categories.
- Allocations.
- Budget movements.
- Adjustments.

Publishes:

- Budget period events.
- Category events.
- Allocation events.
- Movement events.

Consumes:

- Assigned transaction events.
- Income received events.

### 9.3 Transaction Service

Responsibilities:

- Manual transaction entry.
- CSV import.
- Duplicate detection.
- Transaction assignment.
- Transaction splitting.

Owns:

- Transactions.
- Import batches.
- Assignment records.

Publishes:

- Transaction imported events.
- Transaction assigned events.
- Transaction split events.
- Transaction ignored events.

Consumes:

- Category reference events.
- Income source reference events.

### 9.4 Income Service

Responsibilities:

- Income sources.
- Expected income.
- Actual income classification.
- Income variance.

Owns:

- Income sources.
- Income expectations.

Publishes:

- Income source events.
- Income expectation events.
- Income received events.

Consumes:

- Credit transaction assignment events.

### 9.5 Reporting Service

Responsibilities:

- Read models for dashboards.
- Monthly summaries.
- Multi-month analysis.
- CSV exports.

Owns:

- Reporting projections.

Publishes:

- Projection updated events, if useful.

Consumes:

- Budgeting events.
- Transaction events.
- Income events.

### 9.6 Web Application

Responsibilities:

- User interface.
- Authentication flow.
- Budget period views.
- Transaction assignment workflow.
- Reporting views.

Suggested implementation:

- React, Next.js, Blazor, or another modern frontend.
- For portfolio clarity, a React frontend backed by both .NET and Go APIs would demonstrate breadth well.

## 10. Data Storage

Recommended storage:

- PostgreSQL for service-owned operational data.
- Kafka for event streaming.
- Optional Redis for caching or background workflow state.
- Object storage for imported CSV files, if preserving originals.

Each service should own its database schema. Cross-service access should happen through APIs or events, not direct table reads.

## 11. Read Models

The reporting service should build query-optimised read models from events.

Suggested read models:

- `monthly_budget_summary`
- `category_monthly_summary`
- `income_monthly_summary`
- `transaction_assignment_summary`
- `cumulative_category_balance`
- `budget_audit_timeline`

## 12. APIs

APIs should be HTTP/JSON for user-driven commands and queries. Kafka should be used for asynchronous integration.

### 12.1 Example Budgeting API

```http
POST /api/budget-periods
GET /api/budget-periods
GET /api/budget-periods/{budgetPeriodId}
POST /api/budget-periods/{budgetPeriodId}/allocations
POST /api/budget-periods/{budgetPeriodId}/movements
POST /api/budget-periods/{budgetPeriodId}/adjustments
POST /api/budget-periods/{budgetPeriodId}/close
```

### 12.2 Example Transaction API

```http
POST /api/transaction-imports
GET /api/transaction-imports/{importBatchId}
POST /api/transactions
GET /api/transactions?budgetPeriodId={id}
POST /api/transactions/{transactionId}/assign
POST /api/transactions/{transactionId}/split
POST /api/transactions/{transactionId}/ignore
```

### 12.3 Example Reporting API

```http
GET /api/reports/monthly-summary?budgetPeriodId={id}
GET /api/reports/category-trends?categoryId={id}&from={date}&to={date}
GET /api/reports/income-variance?from={date}&to={date}
GET /api/reports/audit-timeline?budgetPeriodId={id}
```

## 13. User Interface

### 13.1 Main Views

- Budget dashboard.
- Budget period setup.
- Category management.
- Income planning.
- Transaction import.
- Transaction assignment inbox.
- Category detail.
- Income detail.
- Audit timeline.
- Reports.

### 13.2 Budget Dashboard

The dashboard should show:

- Current period.
- Expected income.
- Actual income.
- Income variance.
- Total planned budget.
- Total actual spending.
- Remaining budget.
- Over-budget categories.
- Cumulative category balances.

### 13.3 Transaction Assignment Inbox

The inbox should show:

- Unassigned transactions.
- Possible duplicates.
- Suggested categories, if implemented.
- Split transaction action.
- Ignore action.
- Assignment history.

### 13.4 Reports

Reports should include:

- Monthly spending by category.
- Category trends.
- Income variance over time.
- Budget vs actual.
- Cumulative balance history.

## 14. Business Rules

- Transactions must never be deleted once committed; they may be corrected, ignored, or superseded.
- Budget movements must not change actual spending.
- Income must not be mixed into spending totals.
- Refunds assigned to a spending category should reduce net spending for that category while preserving gross transaction history.
- Monthly reset categories do not carry forward unused balances by default.
- Cumulative categories carry forward closing balances.
- Closing a budget period should prevent accidental edits.
- Reopening a closed budget period should be audited.
- Every adjustment requires a reason.
- Monetary values must use decimal types, never floating point.
- All stored amounts must include currency.

## 15. Reconciliation

The system should provide a reconciliation view showing:

- Total imported debits.
- Total imported credits.
- Assigned debits.
- Assigned credits.
- Ignored transactions.
- Unassigned transactions.
- Budget movements.
- Adjustments.
- Difference between bank activity and budgeted activity.

This view exists to answer: "Is this month correct?"

## 16. Architecture

### 16.1 Logical Architecture

```text
                    +------------------+
                    |   Web Frontend   |
                    +--------+---------+
                             |
                             v
                    +------------------+
                    |   API Gateway    |
                    +--------+---------+
                             |
       +---------------------+---------------------+
       |                     |                     |
       v                     v                     v
+--------------+      +--------------+      +--------------+
| Budgeting    |      | Transactions |      | Income       |
| Service      |      | Service      |      | Service      |
+------+-------+      +------+-------+      +------+-------+
       |                     |                     |
       +----------+----------+----------+----------+
                  |                     |
                  v                     v
              +--------+           +----------+
              | Kafka  |---------->| Reporting|
              +--------+           | Service  |
                                   +-----+----+
                                         |
                                         v
                                  +-------------+
                                  | Read Models |
                                  +-------------+
```

### 16.2 Event-Driven Pattern

Recommended pattern:

- Commands arrive over HTTP.
- The owning service validates the command.
- The service writes state changes and outbox records in one database transaction.
- An outbox publisher publishes events to Kafka.
- Other services consume events and update their own state or projections.

This avoids losing events when a service updates its database successfully but fails to publish to Kafka.

### 16.3 Kafka Topics

Suggested topics:

- `budgetytzar.budgeting.events`
- `budgetytzar.transactions.events`
- `budgetytzar.income.events`
- `budgetytzar.reporting.events`

Use consumer groups per service:

- `budgeting-service`
- `transaction-service`
- `income-service`
- `reporting-service`

### 16.4 Event Schema Management

Use JSON Schema for event contracts.

Reasons:

- It is quick to understand and inspect.
- It works naturally with JSON event payloads.
- It demonstrates schema ownership, versioning, validation, and compatibility without pretending to have operated a large enterprise schema platform.
- It keeps the project focused on showing practical understanding and delivery speed.

The system should store event schemas in source control and validate events in tests. A schema registry can be added later if it becomes useful, but it is not required for the first implementation.

## 17. Implementation Strategy

### 17.1 Phase 1: Domain and Local MVP

Build one implementation first, preferably .NET because it matches existing experience.

Deliver:

- Budget periods.
- Categories.
- Income sources.
- Manual transactions.
- Transaction assignment.
- Budget movements.
- Monthly dashboard.
- PostgreSQL persistence.
- Unit and integration tests.

### 17.2 Phase 2: Event-Driven Services

Introduce:

- Kafka.
- Outbox pattern.
- Reporting service projections.
- Audit timeline.
- Docker Compose for local infrastructure.

### 17.3 Phase 3: Containerisation and Kubernetes

Introduce:

- Dockerfiles for each service.
- Kubernetes manifests or Helm charts.
- Local Kubernetes support with kind, k3d, or Docker Desktop Kubernetes.
- Health checks.
- Readiness probes.
- Liveness probes.

### 17.4 Phase 4: Cloud Deployment

Deploy to one cloud provider.

Recommended options:

- Azure Kubernetes Service, especially for a .NET-oriented portfolio.
- AWS Elastic Kubernetes Service.
- Google Kubernetes Engine.

Include:

- Managed PostgreSQL.
- Managed Kafka or Kafka-compatible service.
- Container registry.
- Infrastructure as Code.
- CI/CD pipeline.

### 17.5 Phase 5: Go Implementation

Build Go services that implement the same contracts as the .NET services.

Approach options:

- Reimplement one service first, such as Transactions, in Go.
- Reimplement all backend services in Go.
- Run .NET and Go services side by side using the same Kafka contracts.

For job-search value, a side-by-side architecture is compelling:

- .NET Budgeting Service.
- Go Transaction Service.
- Shared event contracts.
- Shared Kubernetes deployment.

## 18. Technology Choices

### 18.1 .NET Stack

- .NET 9 or current LTS .NET version.
- ASP.NET Core Web API.
- Entity Framework Core.
- PostgreSQL provider.
- MassTransit or Confluent Kafka client.
- FluentValidation.
- xUnit or NUnit.
- Testcontainers.
- OpenTelemetry.

### 18.2 Go Stack

- Current stable Go version.
- chi, Gin, Echo, or standard `net/http`.
- pgx for PostgreSQL.
- segmentio/kafka-go or Confluent Kafka Go client.
- testify.
- testcontainers-go.
- OpenTelemetry Go.

### 18.3 Frontend Stack

- TypeScript.
- React.
- Vite or Next.js.
- TanStack Query.
- A charting library such as Recharts.
- Playwright for end-to-end tests.

### 18.4 Infrastructure

- Docker.
- Docker Compose.
- Kubernetes.
- Helm or Kustomize.
- Terraform or Pulumi.
- GitHub Actions.
- PostgreSQL.
- Kafka.
- OpenTelemetry Collector.
- Prometheus and Grafana, or cloud-native monitoring.

## 19. Testing Strategy

### 19.1 Unit Tests

Cover:

- Category balance calculations.
- Monthly reset behaviour.
- Cumulative carry-forward behaviour.
- Budget movement validation.
- Income variance calculations.
- Transaction assignment rules.

### 19.2 Integration Tests

Cover:

- PostgreSQL persistence.
- Outbox publishing.
- Kafka consumer behaviour.
- CSV import.
- Reporting projections.

Use Testcontainers where practical.

### 19.3 Contract Tests

Cover:

- Event schema compatibility.
- API contracts between frontend and backend.
- Shared event behaviour between .NET and Go services.

### 19.4 End-to-End Tests

Cover:

- Create a month.
- Import transactions.
- Assign transactions.
- Move budget to cover overspending.
- View reports across months.

## 20. Observability

The application should include:

- Structured logging.
- Correlation IDs.
- Distributed tracing with OpenTelemetry.
- Metrics for API requests, Kafka publishing, Kafka consuming, projection lag, and failed imports.
- Health endpoints.
- Dashboard for service health.

Important signals:

- Kafka consumer lag.
- Failed event handling.
- Outbox backlog.
- API error rate.
- Projection freshness.

## 21. Security and Privacy

- Use HTTPS in deployed environments.
- Protect APIs with authentication.
- Store secrets outside source control.
- Avoid logging full bank transaction descriptions if they may contain sensitive information.
- Support data export.
- Support full data deletion for the user.
- Encrypt cloud databases at rest.

## 22. Deployment

### 22.1 Local Development

Use Docker Compose for:

- PostgreSQL.
- Kafka.
- Schema registry, if used.
- Backend services.
- Frontend.

### 22.2 Kubernetes

Each service should define:

- Deployment.
- Service.
- ConfigMap.
- Secret references.
- Readiness probe.
- Liveness probe.
- Horizontal Pod Autoscaler where appropriate.

### 22.3 Cloud

Use Infrastructure as Code to provision:

- Kubernetes cluster.
- Container registry.
- PostgreSQL.
- Kafka or Kafka-compatible event streaming.
- DNS.
- TLS certificates.
- Monitoring.

## 23. Portfolio Demonstration

The repository should include:

- Clear README with architecture diagram.
- Local quick-start instructions.
- Screenshots or short demo video.
- API documentation.
- Event contract documentation.
- Explanation of .NET and Go implementations.
- Deployment guide.
- Architecture decision records.
- CI/CD workflow.
- Test coverage summary.

Suggested portfolio narrative:

> BudgetyTzar replaces an error-prone spreadsheet budgeting workflow with an event-driven, auditable budgeting platform. It uses Kafka for asynchronous service integration, PostgreSQL for service-owned data, Kubernetes for deployment, and equivalent .NET and Go implementations to demonstrate language versatility and cloud-native engineering practice.

## 24. Minimum Viable Product Scope

The smallest useful version should include:

- Create budget categories.
- Create a budget period.
- Mark categories as monthly reset or cumulative.
- Enter expected income.
- Enter actual income.
- Manually add transactions.
- Assign transactions to categories.
- Move budget between categories.
- View period dashboard.
- View transaction-level detail.
- View audit history for budget movements and assignments.

## 25. Future Enhancements

- Bank feed integration through Open Banking.
- Automatic category suggestions.
- Recurring transaction detection.
- Forecasting.
- Multi-account support.
- Household sharing.
- Mobile-friendly PWA.
- Receipt attachment.
- Rules engine for transaction assignment.
- Anomaly detection.
- Scenario planning.
