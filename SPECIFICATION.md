# BudgetyTzar Application Specification

## 1. Purpose

BudgetyTzar is a personal budgeting application that replaces a manual spreadsheet with an auditable ledger-backed HTTP API. It tracks planned budget movements, real transactions, transaction allocations, budget reallocations, balances, snapshots, and historical reporting without relying on period resets.

The core product model is intentionally small:

- Budget.
- Budget item.
- Budget adjustment.
- Budget reallocation.
- Transaction.
- Transaction allocation.

The application is also intended to demonstrate senior software engineering capability through equivalent .NET and Go implementations that share the same product model, event contracts, test scenarios, container strategy, Kubernetes deployment model, and cloud architecture.

## 2. Goals

### 2.1 Product Goals

- Reduce manual budgeting effort.
- Preserve transaction-level detail instead of flattening activity into period totals.
- Model the budget as a dated ledger where every budget item can receive debit and credit adjustments and debit and credit transaction allocations.
- Avoid period reset rules that lose track of funds.
- Avoid separate cumulative/reset configuration on budget items.
- Track expected income, expected spending, actual income, actual spending, reallocations, and unallocated transaction value.
- Provide confidence that the budget is correct through audit trails, snapshots, and reconciliation views.
- Support analysis across arbitrary date ranges, not just one sheet at a time.

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
- Scheduled and recurring adjustments are not required for the first version, though the domain should not prevent adding them later.

## 4. Target User

The primary user is an individual who budgets from a spreadsheet-like ledger, records planned adjustments, reviews bank transactions, allocates those transactions to budget items, moves budget between items, and wants clear historical analysis.

The secondary audience is prospective employers reviewing the project as evidence of engineering capability.

## 5. Core Concepts

### 5.1 Budget

A budget is the root container for budget items, budget adjustments, budget reallocations, transactions, transaction allocations, snapshots, and reports.

A budget has:

- Name.
- Currency.

All child amounts in a budget use the budget currency. Multi-currency budgets, exchange rates, and currency conversion are out of scope for the first version.

### 5.2 Budget Item

A budget item is a named ledger bucket within a budget.

Examples:

- Salary.
- Bonus.
- Mortgage.
- Groceries.
- Petrol.
- Eating out.
- Holiday fund.
- Car maintenance.
- Christmas.

Budget items do not have a fixed debit/credit direction. A single budget item can receive:

- Debit budget adjustments.
- Credit budget adjustments.
- Debit transaction allocations.
- Credit transaction allocations.

Budget items do not have a reset/cumulative setting. All budget item balances are cumulative by default and are derived from dated ledger entries.

Budget items may be active or archived. Archived budget items remain visible in historical snapshots and reports where they had activity.

### 5.3 Budget Adjustment

A budget adjustment is a dated planned movement on one budget item. It changes the budget ledger without representing a bank transaction.

Examples:

- Credit Salary by 2,500.00 for expected income.
- Debit Groceries by 500.00 for planned grocery spending.
- Debit Mortgage by 800.00 for planned mortgage spending.
- Credit Groceries by 50.00 to reduce planned grocery need.

A budget adjustment has:

- Budget item.
- Amount.
- Type: debit or credit.
- Date.
- Notes.

Amounts are positive. The type determines whether the adjustment is a debit or a credit.

### 5.4 Budget Reallocation

A budget reallocation is a grouped set of budget adjustments that moves budget between two or more budget items without representing a bank transaction.

A reallocation must:

- Contain two or more budget adjustments.
- Sum to zero.
- Belong to one budget.
- Have a shared reallocation identifier for auditability.
- Have a date and notes.

Example:

- Credit Eating out by 30.00.
- Debit Groceries by 30.00.

This records that budget was moved from Eating out to Groceries. It does not alter actual transaction totals.

### 5.5 Transaction

A transaction is a manually entered financial movement in the current implementation. Future transaction ingestion may add CSV import, bank feeds, and other bulk sources.

A transaction has:

- Amount.
- Type: debit or credit.
- Date.
- Description or notes.
- Source account, if available.
- External reference, if available.
Amounts are positive. The type determines whether the transaction is a debit or a credit.

Transactions belong to a budget. Transactions may be unallocated, partially allocated, fully allocated, or ignored.

### 5.6 Transaction Allocation

A transaction allocation assigns part of a transaction to a budget item.

A transaction allocation has:

- Transaction.
- Budget item.
- Amount.
- Notes.

Amounts are positive. The transaction type determines whether the allocation contributes a debit or credit movement to the budget item. For example, a credit salary transaction allocated to Salary increases the Salary item's credit activity; a debit correction allocated to Salary reduces that item's net credit position.

The sum of allocations for a transaction must not exceed the transaction amount. A transaction can remain partially allocated, with the unallocated amount appearing in snapshots.

### 5.7 Snapshot

A snapshot is the calculated state of a budget as of a date. Budget item balances are planned-vs-actual positions, not pure accounting ledger balances.

A snapshot includes:

- One row per budget item with its balance as of the snapshot date.
- Unbudgeted or unallocated balance.
- Total transaction balance.
- Optional activity totals for a selected date range.

Balance calculation compares the planned budget ledger with the actual transaction ledger:

- Planned credits represent expected incoming value for a budget item.
- Planned debits represent expected outgoing value or planned funding need for a budget item.
- Actual credits come from credit transactions allocated to the budget item.
- Actual debits come from debit transactions allocated to the budget item.
- Snapshot item balance is calculated as actual credits minus planned credits plus planned debits minus actual debits.

A negative balance means expected credit has not yet arrived or planned debit has been exceeded. A positive balance means actual credit has exceeded expectation or planned debit remains available.

## 6. Functional Requirements

### 6.1 Budget Setup

The user can:

- Create a budget.
- Create budget items.
- Archive budget items that are no longer used.
- Record dated debit and credit budget adjustments.
- Record budget reallocations as grouped zero-sum adjustments.

Acceptance criteria:

- A budget item is created with a name only; it does not require a direction or rollover type.
- Archived budget items remain visible in historical snapshots and reports where they had activity.
- Archived budget items can still be used for retrospective corrections when needed for audit accuracy.
- Budget state can be recalculated from dated adjustments, reallocations, transactions, and transaction allocations.

### 6.2 Budget Adjustments and Reallocations

The user can:

- Record a debit or credit adjustment against any budget item.
- Record expected income as credit adjustments.
- Record expected spending as debit adjustments.
- Move budget between items through a reallocation.
- Add notes explaining adjustments and reallocations.

Acceptance criteria:

- Adjustment amounts must be positive.
- Adjustment type must be debit or credit.
- Net planned spending must not exceed net planned income for the budget as of the relevant date: budget adjustment credits minus budget adjustment debits must be greater than or equal to zero.
- Reallocations must contain at least two adjustments.
- Reallocation adjustments must sum to zero: reallocation credits must equal reallocation debits.
- Reallocations must not change actual transaction totals.

### 6.3 Transaction Entry and Allocation

The user can:

- Manually add transactions.
- Allocate transactions to budget items.
- Split a single transaction across multiple budget items.
- Leave transactions unallocated or partially allocated until they are classified.
- Mark transactions as ignored when they are not relevant to the budget.

Acceptance criteria:

- Transaction amounts must be positive.
- Transaction type must be debit or credit.
- Transaction allocations can be empty, single-item, or split across multiple budget items.
- The sum of transaction allocations must not exceed the transaction amount.
- Debit and credit transactions can be allocated to any budget item.

### 6.4 Budget Tracking

The user can:

- View a budget snapshot as of a date.
- View item balances derived from all activity up to that date.
- View unallocated transaction value.
- View total transaction balance.
- See a clear distinction between real transactions, budget adjustments, reallocations, and unallocated amounts.

Acceptance criteria:

- A snapshot can be recalculated from persisted ledger entries.
- Budget item balances are cumulative by default.
- No reset operation is required to start a new month or planning cycle.
- Snapshot and audit views do not require period reset state.

### 6.5 Audit Trail

The user can:

- View the history of changes affecting a budget.
- See when transactions were imported, allocated, split, edited, or ignored.
- See when budget was adjusted or reallocated.
- See why adjustments and reallocations were made.

Acceptance criteria:

- The current budget state can be explained from recorded events.
- Phase 1 stores durable local audit records before Kafka-backed audit events are introduced.
- The user can understand an old snapshot without relying on spreadsheet context.

### 6.6 Later Reporting and Analysis

Later phases may allow the user to:

- Compare spending across months or custom date ranges.
- View trends by budget item.
- View expected income against actual income.
- View expected spending against actual spending.
- Export data to CSV.

Acceptance criteria:

- The user can answer: "What was my grocery activity over the last 12 months?"
- The user can answer: "Which budget items consistently need more planned funding?"
- The user can answer: "Did actual income match expected income over the last year?"
- Reports can present period-style summaries as read models over the ledger, not as separate period state.

## 7. Example User Journeys

### 7.1 Set Up an Initial Budget

1. User creates a budget named UK with currency GBP.
2. User creates budget items for Salary, Groceries, Mortgage, and Incidentals.
3. User records dated adjustments for expected salary and planned spending.
4. System validates that net planned spending does not exceed net planned income.
5. User views a snapshot as of the adjustment date.

### 7.2 Receive and Allocate Salary

1. User records or imports a 3,000.00 credit transaction.
2. User allocates the transaction to Salary.
3. System updates the Salary budget item balance.
4. Any credit value not absorbed by planned budget need appears as unbudgeted balance.

### 7.3 Allocate Spending

1. User records or imports a 200.00 debit transaction.
2. User allocates 150.00 to Groceries and 40.00 to Incidentals.
3. System leaves 10.00 unallocated.
4. User sees updated item balances and unallocated value in the snapshot.

### 7.4 Plan a Future Income Event

1. User records a future-dated credit adjustment for the next salary payment.
2. A snapshot before that date is unchanged.
3. A snapshot on or after that date includes the expected income adjustment.

### 7.5 Move Budget Between Items

1. User decides Groceries needs 30.00 more planned funding.
2. User records a reallocation containing a credit adjustment to Eating out and a debit adjustment to Groceries.
3. System validates that the grouped adjustments sum to zero.
4. Actual transaction totals remain unchanged.

## 8. Domain Events

The system should use domain events as the source of integration between services. Event names should be stable and versioned.

### 8.1 Event Naming

Recommended format:

```text
budgetytzar.<bounded-context>.<event-name>.v1
```

Examples:

- `budgetytzar.budgeting.budget-created.v1`
- `budgetytzar.budgeting.budget-item-created.v1`
- `budgetytzar.budgeting.budget-adjustment-recorded.v1`
- `budgetytzar.budgeting.budget-reallocation-recorded.v1`
- `budgetytzar.transactions.transaction-created.v1`
- `budgetytzar.transactions.transaction-allocation-recorded.v1`

### 8.2 Core Events

Budgeting events:

- `BudgetCreated`
- `BudgetItemCreated`
- `BudgetItemArchived`
- `BudgetAdjustmentRecorded`
- `BudgetReallocationRecorded`

Transaction events:

- `TransactionManuallyCreated`
- `TransactionAllocationRecorded`
- `TransactionAllocationsReplaced`
- `TransactionIgnored`
- `TransactionEdited`

Reporting events:

- `BudgetSnapshotCalculated`
- `BudgetProjectionUpdated`
- `BudgetItemTrendCalculated`

### 8.3 Event Envelope

All events should share a common envelope:

```json
{
  "eventId": "uuid",
  "eventType": "budgetytzar.transactions.transaction-manually-created.v1",
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

#### BudgetAdjustmentRecorded

```json
{
  "budgetAdjustmentId": "uuid",
  "budgetId": "uuid",
  "budgetItemId": "uuid",
  "amount": "500.00",
  "type": "Debit",
  "date": "2026-06-18T00:00:00Z",
  "notes": "Initial budget for groceries."
}
```

#### BudgetReallocationRecorded

```json
{
  "budgetReallocationId": "uuid",
  "budgetId": "uuid",
  "date": "2026-06-20T00:00:00Z",
  "notes": "Move planned budget to groceries.",
  "adjustments": [
    {
      "budgetItemId": "uuid",
      "amount": "30.00",
      "type": "Credit"
    },
    {
      "budgetItemId": "uuid",
      "amount": "30.00",
      "type": "Debit"
    }
  ]
}
```

#### TransactionAllocationRecorded

```json
{
  "transactionAllocationId": "uuid",
  "transactionId": "uuid",
  "budgetId": "uuid",
  "budgetItemId": "uuid",
  "amount": "150.00",
  "notes": "Partially allocate to groceries."
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
- Budget items.
- Budget adjustments.
- Budget reallocations.
- Snapshot rules for planned budget state.

Owns:

- Budgets.
- Budget items.
- Budget adjustments.
- Budget reallocations.

Publishes:

- Budget events.
- Budget item events.
- Adjustment events.
- Reallocation events.

Consumes:

- Transaction allocation events for balance projections, if projections are service-local.

### 9.3 Transaction Service

Responsibilities:

- Manual transaction entry.
- Transaction allocation.
- Transaction splitting.

Owns:

- Transactions.
- Transaction allocations.

Publishes:

- Transaction manually created events.
- Transaction allocation events.
- Transaction ignored events.
- Transaction edited events.

Consumes:

- Budget item reference events.

### 9.4 Reporting Service

Responsibilities:

- Read models for snapshots.
- Date-range summaries.
- Reconciliation.
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
- Budget snapshot views.
- Budget item management.
- Transaction entry and allocation workflow.
- Reporting views.

Suggested implementation:

- React, Next.js, Blazor, or another modern frontend.
- For portfolio clarity, a React frontend backed by both .NET and Go APIs would demonstrate breadth well.

## 10. Data Storage

Recommended storage:

- PostgreSQL for service-owned operational data.
- Kafka for event streaming.
- Optional Redis for caching or background workflow state.
- Object storage for imported CSV files, if a future import workflow preserves originals.

Each service should own its database schema. Cross-service access should happen through APIs or events, not direct table reads.

## 11. Read Models

The reporting service should build query-optimised read models from events.

Suggested read models:

- `budget_snapshot`
- `budget_item_balance`
- `budget_item_activity_summary`
- `transaction_allocation_summary`
- `unallocated_transaction_summary`
- `budget_audit_timeline`

## 12. APIs

APIs should be HTTP/JSON for user-driven commands and queries. Kafka should be used for asynchronous integration.

### 12.1 Example Budgeting API

```http
POST /api/budgets
GET /api/budgets
GET /api/budgets/{budgetId}
POST /api/budgets/{budgetId}/budget-items
GET /api/budgets/{budgetId}/budget-items
POST /api/budgets/{budgetId}/budget-items/{budgetItemId}/archive
POST /api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments
GET /api/budgets/{budgetId}/budget-items/{budgetItemId}/adjustments
POST /api/budgets/{budgetId}/reallocations
GET /api/budgets/{budgetId}/reallocations
GET /api/budgets/{budgetId}/snapshot?date={date}
```

### 12.2 Example Transaction API

```http
POST /api/budgets/{budgetId}/transactions
GET /api/budgets/{budgetId}/transactions?from={date}&to={date}&allocationStatus={status}
GET /api/budgets/{budgetId}/transactions/{transactionId}
PUT /api/budgets/{budgetId}/transactions/{transactionId}
PUT /api/budgets/{budgetId}/transactions/{transactionId}/allocations
GET /api/budgets/{budgetId}/transactions/{transactionId}/allocations
DELETE /api/budgets/{budgetId}/transactions/{transactionId}/allocations
POST /api/budgets/{budgetId}/transactions/{transactionId}/ignore
```

### 12.3 Example Reporting API

```http
GET /api/budgets/{budgetId}/snapshot?date={date}
```

### 12.4 Example Snapshot Response

```json
{
  "budgetItems": [
    {
      "id": "budget-item-guid-1",
      "name": "Salary",
      "balance": "500.00"
    },
    {
      "id": "budget-item-guid-2",
      "name": "Groceries",
      "balance": "-350.00"
    }
  ],
  "unbudgetedBalance": "190.00",
  "totalBalance": "2800.00"
}
```

## 13. User Interface

### 13.1 Main Views

- Budget snapshot.
- Budget item management.
- Budget adjustment entry.
- Budget reallocation entry.
- Transaction entry.
- Transaction allocation inbox.
- Budget item detail.
- Audit timeline.
- Reports.

### 13.2 Budget Snapshot

The snapshot should show:

- Snapshot date.
- Budget item balances.
- Total budgeted balance.
- Unbudgeted or unallocated balance.
- Total transaction balance.
- Items with negative or unexpected balances.

### 13.3 Transaction Allocation Inbox

The inbox should show:

- Unallocated and partially allocated transactions.
- Suggested budget items, if implemented.
- Split allocation action.
- Ignore action.
- Allocation history.

### 13.4 Reports

Reports should include:

- Activity by budget item.
- Budget item trends.
- Expected vs actual income.
- Expected vs actual spending.
- Budget vs actual.
- Balance history.

## 14. Business Rules

- Monetary values must use decimal types, never floating point.
- Each budget has one currency; all child amounts use the budget currency.
- Amounts must be positive.
- Debit or credit type determines sign.
- Budget item balances are cumulative and are calculated from dated ledger entries.
- Budget items do not have fixed debit/credit direction.
- Budget items do not have period reset or cumulative configuration.
- Net planned spending must not exceed net planned income across budget adjustments: budget adjustment credits minus budget adjustment debits must be greater than or equal to zero.
- A budget reallocation must contain two or more budget adjustments.
- A budget reallocation's adjustments must sum to zero: reallocation credits must equal reallocation debits.
- Budget reallocations must not change actual transaction totals.
- Transactions must never be deleted once committed; they may be corrected, ignored, or superseded.
- The sum of transaction allocations for a transaction must not exceed the transaction amount.
- Transactions may be unallocated, partially allocated, fully allocated, or ignored.
- Debit and credit transactions may be allocated to any budget item.
- Archiving a budget item prevents normal future use but must not prevent audited retrospective corrections where needed.
- Every adjustment and reallocation should include notes.

## 15. Reconciliation

The system should provide a reconciliation view over a date range showing:

- Total transaction debits.
- Total transaction credits.
- Allocated debits.
- Allocated credits.
- Ignored transactions.
- Unallocated transaction value.
- Budget adjustments.
- Budget reallocations.
- Difference between transaction activity and allocated activity.

This view exists to answer: "Is this date range correct?"

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

## 17. Product Versioning

BudgetyTzar should use one product-wide semantic version for the repository and released application, following SemVer 2.0.0:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

Version rules:

- Increment `MAJOR` for incompatible public HTTP API behaviour, event-contract behaviour, or release packaging changes.
- Increment `MINOR` for backward-compatible functionality.
- Increment `PATCH` for backward-compatible fixes.
- Use Conventional Commits to describe release intent: `feat` maps to `MINOR`, `fix` and `perf` map to `PATCH`, and `!` or a `BREAKING CHANGE:` footer maps to `MAJOR` once the product reaches `1.0.0`.
- Commit types `refactor`, `docs`, `test`, `build`, `ci`, `chore`, `style`, and `revert` do not imply a product version bump unless they include breaking-change notation.
- Pre-`1.0.0` releases may evolve faster, but breaking changes must still be documented.
- Event schema names remain independently versioned with suffixes such as `.v1` and `.v2`; the product SemVer records which contract versions ship together.
- Database migrations do not automatically require a major version unless they remove or change supported behaviour incompatibly.

Release requirements:

- Git release tags should use SemVer tags such as `v0.1.0`, `v0.2.0`, and `v1.0.0`; these tags are the canonical released versions.
- Builds should generate version metadata from the latest reachable tag and subsequent Conventional Commits. Tagged commits expose the tag version; commits after a tag expose deterministic preview metadata.
- The runtime API should expose product version metadata separately from health status.
- OpenAPI metadata should include the product SemVer.
- Container image tags introduced in Phase 3 should include explicit SemVer tags such as `budgetytzar-api:0.2.0`; `latest` may exist only as a convenience tag and must not be the release identity.
- Kubernetes manifests or Helm values should reference explicit SemVer image tags.
- GitHub Releases should be the human changelog and release-notes surface.
- Generated version metadata and generated release-note files should be excluded from source control.
- Local development should provide a versioned commit-message hook that validates Conventional Commits without requiring Node tooling.

## 18. Implementation Strategy

### 18.1 Phase 1: Domain and Local MVP

Build one implementation first, preferably .NET because it matches existing experience.

Deliver:

- Budgets.
- Budget items.
- Archived budget item history.
- Budget adjustments with debit/credit type, date, and notes.
- Budget reallocations as grouped zero-sum adjustments.
- Manual transactions.
- Transaction allocations.
- Partial transaction allocation and unallocated value.
- Durable local audit records for transaction creation, allocations, splits, ignores, reallocations, adjustments, and budget item archival.
- Snapshot by date.
- Audit timeline.
- PostgreSQL persistence.
- Unit and integration tests.

### 18.2 Phase 2: Event-Driven Services

Introduce:

- Kafka.
- Outbox pattern.
- Domain-contract-shaped events.
- Projection-backed snapshots and audit timelines.
- Docker Compose for local infrastructure.
- Scheduled and recurring adjustments or reallocations, if still needed.

### 18.3 Phase 2.5: Semantic Versioning and Release Metadata

Introduce semantic versioning before containerisation and Kubernetes work begins.

Deliver:

- Product-wide SemVer source of truth.
- Git release tag convention.
- Runtime version endpoint.
- OpenAPI version metadata.
- GitHub Release notes workflow.
- Build validation that rejects invalid SemVer values.
- Conventional Commits documentation and local commit-message validation.

Phase 2.5 must be complete before Phase 3 so Docker image tags, Kubernetes manifests, deployment documentation, and future cloud release artefacts can use stable version identity.

### 18.4 Phase 3: Containerisation and Kubernetes

Introduce:

- Dockerfiles for each service.
- Kubernetes manifests or Helm charts.
- Explicit SemVer image tags for release builds.
- Local Kubernetes support with kind, k3d, or Docker Desktop Kubernetes.
- Health checks.
- Readiness probes.
- Liveness probes.

### 18.5 Phase 4: Cloud Deployment

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

### 18.6 Phase 5: Go Implementation

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

## 19. Technology Choices

### 19.1 .NET Stack

- .NET 9 or current LTS .NET version.
- ASP.NET Core Web API.
- Entity Framework Core.
- PostgreSQL provider.
- MassTransit or Confluent Kafka client.
- FluentValidation.
- xUnit or NUnit.
- Testcontainers.
- OpenTelemetry.

### 19.2 Go Stack

- Current stable Go version.
- chi, Gin, Echo, or standard `net/http`.
- pgx for PostgreSQL.
- segmentio/kafka-go or Confluent Kafka Go client.
- testify.
- testcontainers-go.
- OpenTelemetry Go.

### 19.3 Frontend Stack

- TypeScript.
- React.
- Vite or Next.js.
- TanStack Query.
- A charting library such as Recharts.
- Playwright for end-to-end tests.

### 19.4 Infrastructure

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

## 20. Testing Strategy

### 20.1 Unit Tests

Cover:

- Budget item balance calculations.
- Adjustment sign handling.
- Net planned spending validation.
- Budget reallocation zero-sum validation.
- Transaction allocation total validation.
- Archived budget item historical correction rules.
- Snapshot calculation.

### 20.2 Integration Tests

Phase 1 integration tests should cover:

- PostgreSQL persistence.
- PostgreSQL-backed API integration tests for provider-specific schema, precision, query, and index behaviour.
- Transaction allocation.
- Snapshot queries.
- Outbox publishing in Phase 2.
- Kafka consumer behaviour in Phase 2.
- Reporting projections in Phase 2.
- Runtime version endpoint and OpenAPI version metadata in Phase 2.5.

Later reporting coverage should include reconciliation calculations, date-range reports, and CSV export once those capabilities are introduced.

Use Testcontainers where practical. SQLite or in-memory tests may be used for fast feedback, but they do not replace PostgreSQL integration coverage for Phase 1 persistence requirements.

### 20.3 Contract Tests

Cover:

- Event schema compatibility.
- API contracts between frontend and backend.
- Shared event behaviour between .NET and Go services.

### 20.4 End-to-End Tests

Cover:

- Create a budget.
- Create budget items.
- Record budget adjustments.
- Import transactions.
- Allocate transactions.
- Reallocate budget.
- View snapshots and reports.

## 21. Observability

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

## 22. Security and Privacy

- Use HTTPS in deployed environments.
- Protect APIs with authentication.
- Store secrets outside source control.
- Avoid logging full bank transaction descriptions if they may contain sensitive information.
- Support data export.
- Support full data deletion for the user.
- Encrypt cloud databases at rest.

## 23. Deployment

### 23.1 Local Development

Use Docker Compose for:

- PostgreSQL.
- Kafka.
- Schema registry, if used.
- Backend services.
- Frontend.

### 23.2 Kubernetes

Each service should define:

- Deployment.
- Service.
- ConfigMap.
- Secret references.
- Readiness probe.
- Liveness probe.
- Horizontal Pod Autoscaler where appropriate.
- Explicit SemVer image tag for release deployments.

### 23.3 Cloud

Use Infrastructure as Code to provision:

- Kubernetes cluster.
- Container registry.
- PostgreSQL.
- Kafka or Kafka-compatible event streaming.
- DNS.
- TLS certificates.
- Monitoring.

## 24. Portfolio Demonstration

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
- SemVer release tags.
- Changelog or release notes grouped by SemVer.
- Visible runtime version metadata.
- Test coverage summary.

Suggested portfolio narrative:

> BudgetyTzar replaces an error-prone spreadsheet budgeting workflow with an event-driven, auditable budgeting platform. It models personal budgets as dated ledger entries, uses Kafka for asynchronous service integration, PostgreSQL for service-owned data, Kubernetes for deployment, and equivalent .NET and Go implementations to demonstrate language versatility and cloud-native engineering practice.

## 25. Minimum Viable Product Scope

The smallest useful version should include:

- Create a budget.
- Create budget items.
- Preserve historical reporting for archived budget items.
- Allow audited retrospective corrections for archived budget items where needed.
- Record debit and credit budget adjustments.
- Record budget reallocations as grouped zero-sum adjustments.
- Manually add transactions.
- Allocate transactions to budget items.
- Leave transactions unallocated or partially allocated until classification.
- View snapshot by date.
- View transaction-level detail.
- View durable local audit history for transaction creation, allocations, splits, ignores, reallocations, adjustments, and archival.

## 26. Future Enhancements

- CSV transaction import with preview, commit, column mapping, and selectable rows.
- Duplicate detection for manual entry, bulk import, and external transaction feeds.
- Scheduled and recurring adjustments.
- Scheduled and recurring reallocations.
- Bank feed integration through Open Banking.
- Automatic budget item suggestions.
- Recurring transaction detection.
- Forecasting.
- Multi-account support.
- Household sharing.
- Mobile-friendly PWA.
- Receipt attachment.
- Rules engine for transaction allocation.
- Anomaly detection.
- Scenario planning.
