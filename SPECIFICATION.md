# BudgetyTzar Application Specification

## 1. Purpose

BudgetyTzar is a personal budgeting application that helps users plan how they intend to use their money, record what actually happened, and understand the difference between the two.

The application treats a budget as a financial plan. A budget defines planned funding and planned spending for a budgeting period, while financial transactions represent real-world activity that occurs independently of the plan. Transactions can be allocated to budget items to compare planned amounts with actual income and expenditure.

The domain model aims to remain simple, expressive, and aligned with the ubiquitous language of personal budgeting. It should accurately represent the core concepts of budgeting while providing a solid foundation for future capabilities such as budget reallocations, historical budget versions, split transaction allocations, and richer reporting, without introducing unnecessary complexity into the initial model.

The core product model is intentionally small:

- Budget.
- Budget item.
- Transaction.
- Transaction allocation.

## 2. Goals

### 2.1 Product Goals

- Provide an intuitive way to create and manage personal budgets.
- Help users compare planned income and expenditure with actual financial activity.
- Make it easy to understand where money is expected to come from, where it is intended to be spent, and where it was actually spent.

### 2.2 Engineering Goals

- Build a maintainable, well-structured codebase with clear architectural boundaries and high cohesion.
- Model the budgeting domain using expressive ubiquitous language and domain-driven design principles.
- Keep the domain model simple while making business invariants explicit and illegal states difficult to represent.
- Evolve the architecture incrementally through small, reviewable changes rather than large-scale redesigns.
- Prefer clarity and correctness over unnecessary abstraction or premature optimisation.
- Design for observability so that the application's behaviour can be understood through logging, metrics, tracing, and health monitoring.
- Design for reliability and resilience through appropriate validation, error handling, idempotency, and testing.
- Design the application so that future capabilities can be introduced without requiring fundamental changes to the core domain model.

## 3. Target User

Individuals who want to proactively plan their finances by creating budgets and comparing planned income and expenditure with actual financial activity.

## 4. Core Concepts

### 4.0 Ubiquitous Language

Budget
: A financial plan for a budgeting period. A budget defines the planned funding and planned consumption for that period. It does not own transactions, which represent real-world financial activity.

Budget Item
: A named element of a budget representing either a source of funding or an area of planned consumption. Each budget item has a planned amount.

Funding
: A budget item representing expected income or other sources of funds, such as salary, bonuses, interest, or transfers.

Consumption
: A budget item representing planned expenditure, such as groceries, mortgage, utilities, transport, entertainment, or savings goals.

Planned Amount
: The amount assigned to a budget item when the budget is created or updated. Funding planned amounts represent expected funding. Consumption planned amounts represent planned spending.

Actual Amount
: The total value of transactions allocated to a budget item within the budgeting period.

Remaining Amount
: The difference between a budget item's planned amount and its actual amount.

Transaction
: A record of real-world financial activity, independent of any budget. A transaction represents money received or spent.

Transaction Allocation
: The association between a transaction and a budget item. Allocations allow actual financial activity to be compared with the original budget plan.

Budget Summary
: A report that compares the planned amounts in a budget with the actual values derived from allocated transactions. It presents funding and consumption separately and highlights the remaining planned amounts and overall budget position.

## 4.1 Budget

A budget represents a financial plan for a budgeting period.

A budget contains one or more budget items that define the planned funding and planned consumption for that period. Transactions are not owned by a budget and represent real-world financial activity independently of the budget.

A budget has:

- Name.
- Currency.
- Budget items.

The total planned funding must always be greater than or equal to the total planned consumption.

All monetary values within a budget use the same currency. Multi-currency budgeting is out of scope for the initial version.

## 4.2 Budget Item

A budget item represents a planned source of funding or a planned area of consumption within a budget.

Examples include:

- Salary
- Bonus
- Mortgage
- Groceries
- Utilities
- Transport
- Entertainment
- Holiday
- Car Maintenance

Each budget item has:

- A name.
- A kind (`Funding` or `Consumption`).
- A planned amount.

The kind determines the semantic purpose of the budget item and never changes as a result of actual financial activity. Refunds, corrections, underpayments, and overpayments affect the item's actual amount but do not change its kind.

## 4.3 Transaction

A transaction represents a real-world financial event.

Transactions exist independently of budgets and record money received or money spent. They may later be allocated to budget items to compare actual financial activity with the original budget.

Each transaction has:

- Amount.
- Currency
- Type (`Credit` or `Debit`).
- Transaction date.
- Description.

Transaction amounts are always positive. The transaction type determines whether the transaction represents money received or money spent.

## 4.4 Transaction Allocation

A transaction allocation associates a transaction with a budget item.

Allocations provide the relationship between real-world financial activity and the budget plan, allowing planned amounts to be compared with actual amounts.

Initially, a transaction may be allocated to at most one budget item. Support for split allocations may be introduced in a future version.

## 4.5 Budget Summary

The Budget Summary compares the planned amounts defined by a budget with the actual amounts derived from allocated transactions.

The summary presents funding and consumption separately.

Each funding and consumption item includes:

- Name.
- Planned amount.
- Actual amount.
- Remaining amount.

Where:

- **Planned Amount** is the amount defined by the budget.
- **Actual Amount** is the total value of allocated transactions for the budget item.
- **Remaining Amount** is the difference between the planned amount and the actual amount.

The summary also includes:

- Total planned funding.
- Total actual funding.
- Total remaining funding.
- Total planned consumption.
- Total actual consumption.
- Total remaining consumption.
- Overall planned surplus.
- Overall actual surplus.

The Budget Summary provides the primary view of progress against a budget by comparing the financial plan with actual financial activity.

## 5. Functional Requirements

### 5.1 Budget Management

The system must allow a user to create a budget.

A budget must have:

* A name.
* A currency.

A budget represents a financial plan. It does not represent a bank account, ledger, or transaction history.

The system must allow a user to view a list of budgets.

The system must allow a user to view the details of a single budget, including its budget items and budget summary.

The system must allow a user to rename a budget.

All planned amounts within a budget use the budget currency.

### 5.2 Budget Item Management

The system must allow a user to add budget items to a budget.

Each budget item must have:

* A name.
* A kind.
* A planned amount.

A budget item kind must be either:

* `Funding`
* `Consumption`

A `Funding` item represents expected funding, such as salary, bonus, interest, or other income.

A `Consumption` item represents planned spending, such as groceries, mortgage, transport, eating out, holidays, or subscriptions.

A budget item planned amount must be positive.

The system must allow a user to rename a budget item.

The system must allow a user to change a budget item planned amount.

A budget item kind must not change after the budget item has been created.

The system must allow a user to delete a budget item from a budget.

The system must prevent a budget item from being deleted while any transaction is allocated to it.

Refunds, corrections, reversals, underpayments, and overpayments must not change the kind of a budget item.

The system must ensure that total planned funding is greater than or equal to total planned consumption.

Deficit budgets are out of scope for the first version.

### 5.3 Transaction Management

The system must allow a user to record transactions.

A transaction must have:

* A description.
* A type.
* A transaction date.
* An amount.
* A currency.

A transaction type must be either:

* `Credit`
* `Debit`

A transaction amount must be positive.

The transaction type records the direction of real-world financial activity. It does not determine whether the transaction is funding or consumption.

Transactions are independent of budgets.

A transaction may exist without being allocated to a budget item.

Unallocated transactions must not affect budget actuals, remaining amounts, or budget summary totals.

### 5.4 Transaction Allocation

The system must allow a user to allocate a transaction to a budget item.

A transaction allocation links a transaction to a budget item.

For the first version, a transaction may be allocated to at most one budget item.

For the first version, an allocation applies the full transaction amount to the selected budget item.

Partial allocations and split allocations are out of scope for the first version.

The system must allow a user to remove a transaction allocation.

The system must prevent a transaction from being allocated to more than one budget item.

The system must prevent a transaction from being allocated to a budget item that does not exist.

The system must prevent a transaction from being allocated to a budget item if the transaction currency does not match the budget currency.

### 5.5 Actual Amount Calculation

Actual amounts are derived from allocated transactions.

Actual amounts must be calculated using both:

* The budget item kind.
* The transaction type.

For `Funding` items:

* `Credit` transactions increase actual funding.
* `Debit` transactions decrease actual funding.

For `Consumption` items:

* `Debit` transactions increase actual consumption.
* `Credit` transactions decrease actual consumption.

This allows refunds, corrections, reversals, underpayments, and overpayments to be represented without changing the budget item kind.

Actual amounts may be less than zero where reversals or corrections exceed the original allocated amount.

Actual amounts may exceed planned amounts.

### 5.6 Remaining Amount Calculation

Remaining amounts are calculated as:

```text
RemainingAmount = PlannedAmount - ActualAmount
```

For `Funding` items, the remaining amount represents expected funding that has not yet been received.

For `Consumption` items, the remaining amount represents planned spending capacity that has not yet been consumed.

A negative remaining amount is allowed.

For `Funding` items, a negative remaining amount means more funding was received than planned.

For `Consumption` items, a negative remaining amount means spending exceeded the planned amount.

### 5.7 Budget Summary

The system must provide a budget summary for a budget.

The budget summary is the primary reporting view.

The budget summary must present funding and consumption separately.

For each funding item, the budget summary must show:

* Name.
* Planned amount.
* Actual amount.
* Remaining amount.

For each consumption item, the budget summary must show:

* Name.
* Planned amount.
* Actual amount.
* Remaining amount.

The budget summary must show funding totals:

* Total planned funding.
* Total actual funding.
* Total remaining funding.

The budget summary must show consumption totals:

* Total planned consumption.
* Total actual consumption.
* Total remaining consumption.

The budget summary must show overall budget-level values, including:

* Planned surplus.
* Actual surplus.
* Remaining funding.
* Remaining consumption capacity.

Planned surplus is calculated as:

```text
TotalPlannedFunding - TotalPlannedConsumption
```

Actual surplus is calculated as:

```text
TotalActualFunding - TotalActualConsumption
```

The budget summary is a read model. It does not need to mirror the aggregate structure exactly, but it must remain consistent with the domain language.

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
- Transaction allocation replacement.

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

### 9.5 Audit Service

Responsibilities:

- Durable audit records.
- Audit timelines.
- Audit event projection.
- Audit event failure tracking.

Owns:

- Audit records.
- Audit projections.
- Audit event processing state.

Publishes:

- Audit events, if useful.

Consumes:

- Budgeting events.
- Transaction events.

### 9.6 Web Application

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

## 12. APIs

APIs should be HTTP/JSON for user-driven commands and queries.

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
- Metrics for API requests.
- Health endpoints.
- Dashboard for service health.

Important signals:

- API error rate.

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
- DNS.
- TLS certificates.
- Monitoring.

## 24. Portfolio Demonstration

The repository should include:

- Clear README with architecture diagram.
- Local quick-start instructions.
- API documentation.
- Explanation of implementation.
- Deployment guide.
- CI/CD workflow.
- SemVer release tags.
- Changelog or release notes grouped by SemVer.
- Visible runtime version metadata.
- Test coverage summary.

Suggested portfolio narrative:

> BudgetyTzar replaces an error-prone spreadsheet budgeting workflow with an event-driven, auditable budgeting platform. It models personal budgets as dated ledger entries, uses Kafka for asynchronous service integration, PostgreSQL for service-owned data, Kubernetes for deployment, and equivalent .NET and Go implementations to demonstrate language versatility and cloud-native engineering practice.