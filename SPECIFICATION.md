# BudgetyTzar Application Specification

## 1. Purpose

BudgetyTzar is a personal budgeting application that replaces a manual period-based spreadsheet process with an auditable, event-driven system. It tracks planned credits, actual credits, planned debits, actual debits, budget allocations, spending, credits, reallocations between budget lines, and cumulative budget lines across multiple periods.

The application is also intended to demonstrate senior software engineering capability through two equivalent implementations:

- A .NET implementation.
- A Go implementation.

Both implementations should use the same product model, event contracts, test scenarios, container strategy, Kubernetes deployment model, and cloud architecture.

## 2. Goals

### 2.1 Product Goals

- Reduce manual budgeting effort.
- Preserve transaction-level detail instead of flattening everything into a single period spent column.
- Clearly distinguish planned credits, actual credits, planned debits, actual debits, budget allocations, and budget reallocations.
- Support budget lines that reset each period and budget lines that accumulate over time.
- Track planned credits against actual credits.
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
- Mixed-currency budgets are not required for the first version.

## 4. Target User

The primary user is an individual who budgets by period, reviews bank transactions, allocates those transactions to budget lines, adjusts budget during the period, and wants clear historical analysis.

The secondary audience is prospective employers reviewing the project as evidence of engineering capability.

## 5. Core Concepts

### 5.1 Budget

A budget is the root container for periods, budget lines, transactions, allocations, reallocations, and reports.

A budget has:

- Name.
- Currency.

All child amounts in a budget use the budget currency. Multi-currency budgets, exchange rates, and currency conversion are out of scope for the first version.

### 5.2 Budget Period

A budget period is a date range within a budget. It often represents one calendar month, but the period is the source of truth rather than the month.

Examples:

- January 2026
- February 2026

A period has:

- Budget.
- Start date.
- End date.
- Opening balances for cumulative debit budget lines.
- Planned credit allocations.
- Planned debit allocations.
- Actual credits.
- Actual debits.
- Closing balances.

Budget periods cannot overlap within the same budget. Periods in different budgets may overlap.

### 5.3 Budget Line

A budget line represents a planned credit or debit line in the budget.

Examples:

- Mortgage
- Groceries
- Petrol
- Eating out
- Holiday fund
- Car maintenance
- Christmas
- Salary
- Bonus

Budget lines have a planning direction:

- `Debit`: used for planned and actual spending.
- `Credit`: used for planned and actual income or other incoming money.

The planning direction defines how the line is budgeted and reported, but it does not prevent assigning a transaction with the opposite direction. Opposite-direction assignments are allowed when they naturally offset the line. For example, a debit medical expenses line may receive debit transactions for up-front payments and credit transactions for insurance reimbursements.

Budget lines have a rollover type:

- `PeriodReset`: unused debit budget does not carry forward automatically.
- `Cumulative`: unused debit budget carries forward into future periods.

Budget lines may be active or archived.

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
- Budget line assignments.
- Notes.

Transactions belong to a budget. A transaction's date determines which budget period includes it in period reports.

### 5.5 Allocation

An allocation sets the planned amount for a budget line in a budget period.

Examples:

- Allocate 400.00 to Groceries for June 2026.
- Allocate 3,000.00 to Salary for June 2026.

### 5.6 Budget Reallocation

A budget reallocation transfers available budget from one debit budget line to another without representing a bank transaction.

Example:

- Move 50.00 from Eating out to Groceries.

Budget reallocations must be stored separately from real transactions.

The source budget line must have enough available balance in the target period for the reallocation amount. Reallocations must not be allowed to create or conceal overspending on the source line.

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

- Create budget lines.
- Mark budget lines as debit or credit.
- Mark budget lines as period reset or cumulative.
- Archive budget lines that are no longer used.
- Configure expected recurring credit and debit allocations.
- Start a new budget period from a template or previous period.

Acceptance criteria:

- A new period can be created with expected credit and debit budget lines pre-populated.
- Cumulative debit budget lines start with their previous closing balance.
- Period reset debit budget lines start from zero unless explicitly allocated.
- Archived budget lines remain visible in historical periods and reports where they had activity.
- Archived budget lines can still be used for retrospective corrections in historical periods where they already had allocation, transaction assignment, reallocation, adjustment, or carried cumulative balance activity.

### 6.2 Transaction Entry and Import

The user can:

- Manually add transactions.
- Import transactions from a CSV file.
- View imported transactions before committing them.
- Detect likely duplicate transactions.
- Assign transactions to budget lines.
- Split a single transaction across multiple budget lines.
- Leave transactions unassigned until they are classified.
- Mark transactions as ignored when they are not relevant to the budget.

Acceptance criteria:

- Imported transaction details remain visible after assignment.
- Debit transactions reduce available debit budget line balances.
- Credit transactions assigned to credit budget lines increase actual credit totals.
- Credit transactions assigned to debit budget lines reduce net spending for those lines, such as refunds or reimbursements.
- Debit transactions assigned to credit budget lines reduce net credit for those lines, such as reversals or corrections.
- Transaction assignments can be empty, single-line, or split across multiple budget lines.

### 6.3 Budget Tracking

The user can:

- View planned credits vs actual credits for a period.
- View planned debits vs actual debits by budget line.
- View current remaining budget by debit budget line.
- See over-budget debit budget lines.
- Reallocate budget between debit budget lines.
- See a clear distinction between real spending, refunds, allocations, transfers, and adjustments.

Acceptance criteria:

- Overspending is visible without needing to inspect formulas.
- Reallocating budget between budget lines does not alter actual spending totals.
- Reallocation commands validate that both budget lines are debit lines in the same budget and that the source line has sufficient available balance in the period.
- Budget line balances can be recalculated from event history.

### 6.4 Cumulative Budget Lines

The user can:

- Carry unused budget forward for cumulative debit budget lines.
- Spend from accumulated balances.
- View opening balance, period allocation, spending, reallocations, adjustments, and closing balance.

Acceptance criteria:

- A cumulative debit budget line's closing balance becomes the next period's opening balance.
- Period reset debit budget line balances do not carry forward by default.

### 6.5 Income Tracking

The user can:

- Define expected credit allocations for a period.
- Assign credit transactions to credit budget lines.
- Compare planned credits with actual credits.
- See credit variance by budget line and by period.

Acceptance criteria:

- The system shows whether actual earnings matched expected earnings.
- Credit totals are not mixed into debit spending totals.

### 6.6 Audit Trail

The user can:

- View the history of changes affecting a budget period.
- See when transactions were imported, assigned, split, edited, or ignored.
- See when budget was reallocated between budget lines.
- See why adjustments were made.

Acceptance criteria:

- The current budget state can be explained from recorded events.
- Phase 1 stores durable local audit records before Kafka-backed audit events are introduced.
- The user can understand an old period without relying on spreadsheet context.
- A period audit timeline includes only events that affected that period, plus explicitly marked budget-level events whose effect genuinely applies to that period.
- Import preview and commit audit records should be linked to affected transaction periods when possible, so unrelated imports do not appear in every period timeline.

### 6.7 Reporting and Analysis

The user can:

- Compare spending across months.
- View trends by budget line.
- View credit trends.
- View budget variance over time.
- Export data to CSV.

Acceptance criteria:

- The user can answer: "How much did I spend on groceries over the last 12 months?"
- The user can answer: "Which budget lines am I consistently overspending?"
- The user can answer: "Did I earn what I expected over the last year?"

## 7. Example User Journeys

### 7.1 Start a New Period

1. User selects "Create Budget Period".
2. User chooses July 2026.
3. System copies recurring credit and debit allocation templates.
4. System carries forward balances for cumulative debit budget lines.
5. User reviews and confirms the new period.

### 7.2 Import and Assign Transactions

1. User uploads a bank CSV.
2. System parses and previews transactions.
3. System flags possible duplicates.
4. User commits the import.
5. User assigns each transaction to one or more budget lines, or leaves it unassigned.
6. System updates projected budget balances.

### 7.3 Cover Overspending

1. User sees Groceries is 30.00 over budget.
2. User reallocates 30.00 from Eating out to Groceries.
3. System records a budget reallocation.
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
- `budgetytzar.budgeting.budget-reallocation-recorded.v1`

### 8.2 Core Events

Budgeting events:

- `BudgetCreated`
- `BudgetPeriodCreated`
- `BudgetLineCreated`
- `BudgetLineArchived`
- `BudgetLineChanged`
- `BudgetLineAllocationPlanned`
- `BudgetLineAllocationChanged`
- `BudgetReallocationRecorded`
- `BudgetAdjustmentRecorded`

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
- `PeriodSummaryCalculated`
- `BudgetLineTrendCalculated`

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
  "budgetId": "uuid",
  "transactionDate": "2026-06-12",
  "description": "SUPERMARKET",
  "amount": "42.30",
  "direction": "Debit",
  "externalReference": "bank-reference"
}
```

#### BudgetReallocationRecorded

```json
{
  "budgetReallocationId": "uuid",
  "budgetPeriodId": "uuid",
  "fromBudgetLineId": "uuid",
  "toBudgetLineId": "uuid",
  "amount": "30.00",
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

- Budgets.
- Budget periods.
- Budget lines.
- Planned allocations.
- Budget reallocations.
- Adjustments.

Owns:

- Budgets.
- Budget periods.
- Budget lines.
- Allocations.
- Budget reallocations.
- Adjustments.

Publishes:

- Budget period events.
- Budget line events.
- Allocation events.
- Reallocation events.

Consumes:

- Assigned transaction events.

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

- Budget line reference events.

### 9.4 Reporting Service

Responsibilities:

- Read models for dashboards.
- Period summaries.
- Multi-month analysis.
- CSV exports.

Owns:

- Reporting projections.

Publishes:

- Projection updated events, if useful.

Consumes:

- Budgeting events.
- Transaction events.

### 9.5 Web Application

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

- `period_budget_summary`
- `budget_line_period_summary`
- `credit_budget_line_period_summary`
- `transaction_assignment_summary`
- `cumulative_budget_line_balance`
- `budget_audit_timeline`

## 12. APIs

APIs should be HTTP/JSON for user-driven commands and queries. Kafka should be used for asynchronous integration.

### 12.1 Example Budgeting API

```http
POST /api/budgets
GET /api/budgets
GET /api/budgets/{budgetId}
POST /api/budgets/{budgetId}/periods
GET /api/budgets/{budgetId}/periods
GET /api/budgets/{budgetId}/periods/{budgetPeriodId}
GET /api/budgets/{budgetId}/periods/for-date?date={date}
POST /api/budgets/{budgetId}/budget-lines
GET /api/budgets/{budgetId}/budget-lines
POST /api/budgets/{budgetId}/budget-lines/{budgetLineId}/archive
PUT /api/budgets/{budgetId}/periods/{budgetPeriodId}/allocations
GET /api/budgets/{budgetId}/periods/{budgetPeriodId}/allocations
POST /api/budgets/{budgetId}/periods/{budgetPeriodId}/reallocations
GET /api/budgets/{budgetId}/periods/{budgetPeriodId}/reallocations
POST /api/budgets/{budgetId}/periods/{budgetPeriodId}/adjustments
GET /api/budgets/{budgetId}/periods/{budgetPeriodId}/adjustments
```

### 12.2 Example Transaction API

```http
POST /api/budgets/{budgetId}/transaction-imports/preview
POST /api/budgets/{budgetId}/transaction-imports/{importBatchId}/commit
GET /api/budgets/{budgetId}/transaction-imports/{importBatchId}
POST /api/budgets/{budgetId}/transactions
GET /api/budgets/{budgetId}/transactions?periodId={id}
GET /api/budgets/{budgetId}/transactions?from={date}&to={date}&assignmentStatus={status}
GET /api/budgets/{budgetId}/transactions/{transactionId}
PUT /api/budgets/{budgetId}/transactions/{transactionId}/assignments
GET /api/budgets/{budgetId}/transactions/{transactionId}/assignments
DELETE /api/budgets/{budgetId}/transactions/{transactionId}/assignments
POST /api/budgets/{budgetId}/transactions/{transactionId}/ignore
```

### 12.3 Example Reporting API

```http
GET /api/budgets/{budgetId}/reports/period-summary?periodId={id}
GET /api/budgets/{budgetId}/reports/budget-line-trends?budgetLineId={id}&from={date}&to={date}
GET /api/budgets/{budgetId}/reports/credit-variance?from={date}&to={date}
GET /api/budgets/{budgetId}/reports/reconciliation?periodId={id}
GET /api/budgets/{budgetId}/reports/audit-timeline?periodId={id}
GET /api/budgets/{budgetId}/reports/period-summary.csv?periodId={id}
```

## 13. User Interface

### 13.1 Main Views

- Budget dashboard.
- Budget period setup.
- Budget line management.
- Transaction import.
- Transaction assignment inbox.
- Budget line detail.
- Audit timeline.
- Reports.

### 13.2 Budget Dashboard

The dashboard should show:

- Current period.
- Planned credits.
- Actual credits.
- Credit variance.
- Planned debits.
- Actual debits.
- Remaining debit budget.
- Unassigned and partially assigned transaction totals.
- Over-budget debit budget lines.
- Cumulative budget line balances.

### 13.3 Transaction Assignment Inbox

The inbox should show:

- Unassigned transactions.
- Possible duplicates.
- Suggested budget lines, if implemented.
- Split transaction action.
- Ignore action.
- Assignment history.

### 13.4 Reports

Reports should include:

- Period spending by budget line.
- Budget line trends.
- Credit variance over time.
- Budget vs actual.
- Cumulative balance history.

## 14. Business Rules

- Transactions must never be deleted once committed; they may be corrected, ignored, or superseded.
- Budget reallocations must not change actual spending.
- Budget reallocations may only move currently available budget between debit budget lines in the same budget, within the selected budget period.
- Credit totals must not be mixed into debit spending totals.
- Debit transactions assigned to debit budget lines increase spending against those lines.
- Credit transactions assigned to debit budget lines reduce net spending against those lines.
- Credit transactions assigned to credit budget lines increase actual credit against those lines.
- Debit transactions assigned to credit budget lines reduce net credit against those lines.
- Transactions may be unassigned, assigned to one budget line, or split across multiple budget lines.
- Period reset debit budget lines do not carry forward unused balances by default.
- Cumulative debit budget lines carry forward closing balances.
- Periods do not have open or closed status in the first version; retrospective changes are allowed and should be visible through audit/history.
- Archiving a budget line prevents normal future use but must not prevent audited retrospective corrections for historical periods where the line already had activity.
- Every adjustment requires a reason.
- Monetary values must use decimal types, never floating point.
- Each budget has one currency; all child amounts use the budget currency.

## 15. Reconciliation

The system should provide a reconciliation view showing:

- Total imported debits.
- Total imported credits.
- Assigned debits.
- Assigned credits.
- Ignored transactions.
- Unassigned transactions.
- Budget reallocations.
- Adjustments.
- Difference between bank activity and budgeted activity.

This view exists to answer: "Is this period correct?"

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
              +--------------------+--------------------+
              |                                         |
              v                                         v
       +--------------+                         +--------------+
       | Budgeting    |                         | Transactions |
       | Service      |                         | Service      |
       +------+-------+                         +------+-------+
              |                                        |
              +--------------------+-------------------+
                                   |
                                   v
                               +--------+
                               | Kafka  |
                               +---+----+
                                   |
                                   v
                              +----------+
                              | Reporting|
                              | Service  |
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
- `budgetytzar.reporting.events`

Use consumer groups per service:

- `budgeting-service`
- `transaction-service`
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

- Budgets.
- Budget periods.
- Budget lines.
- Historical visibility of archived budget lines in old periods.
- Audited retrospective corrections for archived budget lines in historical periods where they had activity.
- Manual transactions.
- CSV import preview and commit.
- Duplicate detection for imported transactions.
- Transaction assignment.
- Opposite-direction assignments for refunds, reimbursements, reversals, and corrections.
- Budget reallocations with available-balance validation.
- Adjustments with reasons.
- Durable local audit records for imports, assignments, splits, ignores, reallocations, adjustments, and budget line archival, with period timelines filtered to events that affected the selected period.
- Period summary.
- Reconciliation view.
- Basic multi-period reports for budget line trends and credit variance.
- CSV export.
- PostgreSQL persistence.
- Unit and integration tests.

### 17.2 Phase 2: Event-Driven Services

Introduce:

- Kafka.
- Outbox pattern.
- Service decomposition with service-owned schemas.
- Projection-backed reporting and audit timelines.
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

- Budget line balance calculations.
- Period reset behaviour.
- Cumulative carry-forward behaviour.
- Budget reallocation validation.
- Archived budget line historical correction rules.
- Credit variance calculations.
- Transaction assignment rules.

### 19.2 Integration Tests

Cover:

- PostgreSQL persistence.
- PostgreSQL-backed API integration tests for provider-specific schema, precision, query, and index behaviour.
- CSV import.
- Duplicate detection.
- Reconciliation.
- CSV export.
- Outbox publishing in Phase 2.
- Kafka consumer behaviour in Phase 2.
- Reporting projections in Phase 2.

Use Testcontainers where practical. SQLite or in-memory tests may be used for fast feedback, but they do not replace PostgreSQL integration coverage for Phase 1 persistence requirements.

### 19.3 Contract Tests

Cover:

- Event schema compatibility.
- API contracts between frontend and backend.
- Shared event behaviour between .NET and Go services.

### 19.4 End-to-End Tests

Cover:

- Create a budget period.
- Import transactions.
- Assign transactions.
- Reallocate budget to cover overspending.
- View reports across periods.

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

- Create a budget.
- Create a budget period.
- Create debit and credit budget lines.
- Mark budget lines as period reset or cumulative.
- Preserve historical reporting for archived budget lines.
- Allow audited retrospective corrections for archived budget lines in periods where they had activity.
- Enter planned credit and debit allocations.
- Manually add transactions.
- Preview and commit CSV imports.
- Detect likely duplicate imported transactions.
- Assign transactions to budget lines.
- Leave transactions unassigned until classification.
- Reallocate available budget between debit budget lines.
- Record adjustments with reasons.
- View period summary.
- View reconciliation for a period.
- View basic reports across periods.
- View transaction-level detail.
- View durable local audit history for imports, assignments, splits, ignores, reallocations, adjustments, and archival, scoped to the selected period where applicable.
- Export data to CSV.

## 25. Future Enhancements

- Bank feed integration through Open Banking.
- Automatic budget line suggestions.
- Recurring transaction detection.
- Forecasting.
- Multi-account support.
- Household sharing.
- Mobile-friendly PWA.
- Receipt attachment.
- Rules engine for transaction assignment.
- Anomaly detection.
- Scenario planning.
