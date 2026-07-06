# BudgetyTzar Application Specification

## 1. Purpose

BudgetyTzar is a personal budgeting application that helps users plan how they intend to use their money, record what actually happened, and understand the difference between the two.

The application treats a budget as a financial plan. A budget defines planned funding and planned spending for a user-defined purpose, while financial transactions represent real-world activity that occurs independently of the plan. Transactions can be allocated to budget items to compare planned amounts with actual income and expenditure.

The domain model aims to remain simple, expressive, and aligned with the ubiquitous language of personal budgeting. It should accurately represent the core concepts of budgeting without introducing unnecessary complexity into the model.

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
- Ensure supported use cases can be verified through automated API-level behaviour tests that exercise the public HTTP API without inspecting backend SQL state.
- Prefer API-level behaviour tests as the primary regression safety net for public behaviour, while using lower-level unit tests selectively where they provide clearer or faster feedback for domain calculations, invariants, or edge cases that are awkward to express through the API.
- Design the application so that the core domain model can evolve without requiring fundamental redesign.

## 3. Target User

Individuals who want to proactively plan their finances by creating budgets and comparing planned income and expenditure with actual financial activity.

## 4. Core Concepts

### 4.0 Ubiquitous Language

Budget
: A financial plan for a user-defined purpose. A budget defines planned funding and planned consumption. It does not own transactions, which represent real-world financial activity.

Budget Item
: A named element of a budget representing either a source of funding or an area of planned consumption. Each budget item has a planned amount.

Funding
: A budget item representing expected income or other sources of funds, such as salary, bonuses, interest, or transfers.

Consumption
: A budget item representing planned expenditure, such as groceries, mortgage, utilities, transport, entertainment, or savings goals.

Planned Amount
: The amount assigned to a budget item when the budget is created or updated. Funding planned amounts represent expected funding. Consumption planned amounts represent planned spending.

Actual Amount
: The effective total derived from transactions allocated to a budget item. The calculation depends on both the budget item kind and the transaction type.

Remaining Amount
: The difference between a budget item's planned amount and its actual amount.

Transaction
: A record of real-world financial activity, independent of any budget. A transaction represents money received or spent.

Transaction Allocation
: The association between a transaction and a budget item. Allocations allow actual financial activity to be compared with the original budget plan.

Budget Summary
: A report that compares the planned amounts in a budget with the actual values derived from allocated transactions. It presents funding and consumption separately and highlights the remaining planned amounts and overall budget position.

## 4.1 Budget

A budget represents a financial plan for a user-defined purpose.

A budget may contain budget items that define planned funding and planned consumption. A budget can exist without budget items, although an empty budget has no useful budgeting value until items are added. Transactions are not owned by a budget and represent real-world financial activity independently of the budget.

A budget has:

- Name.
- Currency.
- Budget items.

The total planned funding must always be greater than or equal to the total planned consumption.

The model does not assign a start date or end date to a budget. Budgets may overlap in real-world time, such as separate household and business budgets for the same calendar month. A transaction's date does not restrict whether it can be allocated to a budget item.

All monetary values within a budget use the same currency. The model supports one currency per budget and does not perform exchange-rate conversion.

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

The kind determines the semantic purpose of the budget item and never changes as a result of actual financial activity. Refunds, corrections, reversals, underpayments, and overpayments affect the item's actual amount but do not change its kind.

## 4.3 Transaction

A transaction represents a real-world financial event.

Transactions exist independently of budgets and record money received or money spent. They may be allocated to budget items to compare actual financial activity with the original budget.

Each transaction has:

- Amount.
- Currency.
- Type (`Credit` or `Debit`).
- Transaction date.
- Description.

Transaction amounts are always positive. The transaction type determines whether the transaction represents money received or money spent.

The transaction date records when the real-world financial activity occurred. It does not constrain allocation to a budget item.

## 4.4 Transaction Allocation

A transaction allocation associates a transaction with a budget item.

Allocations provide the relationship between real-world financial activity and the budget plan, allowing planned amounts to be compared with actual amounts.

A transaction may be allocated to at most one budget item. An allocation applies the full transaction amount to the selected budget item.

Allocations are owned by the Transactions boundary because allocation state is part of transaction usage: a transaction may be unallocated, allocated to one budget item, or have its allocation removed.

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
- **Actual Amount** is the effective total derived from allocated transactions for the budget item. The calculation depends on both the budget item kind and the transaction type.
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

The system must reject budget item changes that would make total planned consumption exceed total planned funding.

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

The system must allow a user to delete a transaction.

The system must prevent a transaction from being deleted while it is allocated to a budget item.

The user must remove the transaction allocation before deleting the transaction.

A deleted transaction must not affect budget actuals, remaining amounts, or budget summary totals.

### 5.4 Transaction Allocation

The system must allow a user to allocate a transaction to a budget item.

A transaction allocation links a transaction to a budget item.

A transaction may be allocated to at most one budget item.

An allocation applies the full transaction amount to the selected budget item.


The system must allow a user to remove a transaction allocation.

The system must prevent a transaction from being allocated to more than one budget item.

If the transaction is already allocated to the same budget item requested by the allocation command, the command must succeed without changing state.

If the transaction is already allocated to another budget item, the command must be rejected.

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

For `Consumption` items, the remaining amount represents planned consumption that has not yet occurred.

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

Planned surplus is calculated as:

```text
TotalPlannedFunding - TotalPlannedConsumption
```

Actual surplus is calculated as:

```text
TotalActualFunding - TotalActualConsumption
```

The Budget Summary is shaped for reporting. It does not need to mirror the internal aggregate structure exactly, but it must remain consistent with the domain language.

## 6. Example User Journeys

### 6.1 Set Up an Initial Budget

1. User creates a budget named `UK` with currency `GBP`.
2. User adds a `Funding` budget item named `Salary` with a planned amount of `3,000.00`.
3. User adds a `Consumption` budget item named `Mortgage` with a planned amount of `1,200.00`.
4. User adds a `Consumption` budget item named `Groceries` with a planned amount of `400.00`.
5. User adds a `Consumption` budget item named `Transport` with a planned amount of `150.00`.
6. User adds a `Consumption` budget item named `Incidentals` with a planned amount of `250.00`.
7. System validates that total planned funding of `3,000.00` is greater than total planned consumption of `2,000.00`.
8. User views the Budget Summary.
9. The Budget Summary shows planned funding and planned consumption separately, with zero actual amounts until transactions are allocated.

### 6.2 Receive and Allocate Salary

1. User records a `3,000.00` GBP `Credit` transaction with the description `Salary`.
2. User allocates the transaction to the `Salary` budget item.
3. System validates that the transaction currency matches the budget currency.
4. System records the allocation.
5. The Budget Summary shows `Salary` with a planned amount of `3,000.00`, an actual amount of `3,000.00`, and a remaining amount of `0.00`.
6. Total actual funding increases by `3,000.00`.

### 6.3 Allocate Spending

1. User records a `200.00` GBP `Debit` transaction with the description `Groceries`.
2. User allocates the transaction to the `Groceries` budget item.
3. System records the full transaction amount against the selected budget item.
4. The Budget Summary shows `Groceries` actual consumption increased by `200.00`.
5. The `Groceries` remaining amount is reduced by `200.00`.

### 6.4 Record a Refund

1. User records a `20.00` GBP `Credit` transaction with the description `Grocery refund`.
2. User allocates the transaction to the `Groceries` budget item.
3. `Groceries` remains a `Consumption` item.
4. System calculates the credit transaction as reducing actual consumption.
5. The Budget Summary shows `Groceries` actual consumption reduced by `20.00`.

### 6.5 Record a Funding Correction

1. User records a `100.00` GBP `Debit` transaction with the description `Salary correction`.
2. User allocates the transaction to the `Salary` budget item.
3. `Salary` remains a `Funding` item.
4. System calculates the debit transaction as reducing actual funding.
5. The Budget Summary shows `Salary` actual funding reduced by `100.00`.

### 6.6 Remove an Allocation

1. User views a transaction that has already been allocated to a budget item.
2. User removes the allocation.
3. The transaction remains recorded.
4. The transaction becomes unallocated.
5. The transaction no longer contributes to budget actual amounts, remaining amounts, or Budget Summary totals.

### 6.7 Delete a Budget Item

1. User attempts to delete a budget item.
2. If no transactions are allocated to the budget item, the system allows the deletion.
3. If any transaction is allocated to the budget item, the system prevents the deletion.
4. User must remove the relevant allocations before deleting the budget item.

## 7. Logical Architecture and Boundaries

The architecture should prioritise clear domain boundaries without requiring unnecessary deployment complexity.

The application may be implemented as a modular monolith, with modules separated by ownership of domain concepts. Physical deployment should follow clear product or operational requirements.

The logical boundaries are:

- Identity.
- Budgeting.
- Transactions.
- Reporting.
- Audit.
- Web application.

The important design concern is ownership of data, rules, and language. Physical deployment should not force additional concepts into the domain model.

### 7.1 Identity Boundary

Responsibilities:

- User authentication.
- User profile identity information.
- Identity claims required by application services.
- Providing an authenticated identity to application and domain boundary services.

Does not own:

- Authorisation decisions for domain resources.
- The ownership model for budgets.
- The ownership model for transactions.
- Domain-specific access rules for budgets, transactions, or allocations.

Suggested implementation:

- Use an external identity provider with OpenID Connect where practical.
- The application should not implement its own password storage or identity management unless there is a deliberate product reason.

Authorisation is a separate concern from authentication. The Identity boundary authenticates the user and supplies identity claims. Domain boundaries use the authenticated identity to enforce ownership and access rules for their own resources.

### 7.2 Budgeting Boundary

Responsibilities:

- Budget creation and management.
- Budget item creation and management.
- Budget access rules, including any ownership model chosen for budgets.
- Budget item planned amount validation.
- Budget item kind validation.
- Enforcement of the rule that total planned funding must be greater than or equal to total planned consumption.
- Prevention of budget item deletion while transactions are allocated to the budget item.

Owns:

- Budgets.
- Budget items.

Does not own:

- Transactions.
- Transaction allocations.

### 7.3 Transactions Boundary

Responsibilities:

- Transaction recording.
- Transaction retrieval.
- Transaction access rules, including any ownership model chosen for transactions.
- Transaction allocation.
- Transaction allocation removal.
- Enforcement that a transaction may be allocated to at most one budget item.
- Enforcement that an allocation applies the full transaction amount.
- Enforcement that transaction currency matches the selected budget item's budget currency.

Owns:

- Transactions.
- Transaction allocations.

Does not own:

- Budgets.
- Budget items.

The Transactions boundary may need to query or reference budget item and budget currency information in order to validate allocations. That dependency should be explicit and should not make transactions children of budgets.

The Reporting boundary needs allocation data to calculate budget summaries, and the Budgeting boundary needs allocation existence checks to prevent deletion of allocated budget items. Those read dependencies must be explicit. They must not move allocation ownership out of the Transactions boundary.

### 7.4 Reporting Boundary

Responsibilities:

- Budget Summary queries.
- Budget-level reporting based on budgets, budget items, transactions, and allocations.
- Reporting responses shaped for user workflows.

Owns:

- Budget Summary query behaviour.
- Report response shapes.

The Reporting boundary should treat the Budget Summary as the primary reporting view. It does not need to mirror the internal aggregate structure exactly, but it must remain consistent with the ubiquitous language.

### 7.5 Audit Boundary

Responsibilities:

- Durable audit records.
- Audit timelines for user-visible changes.
- Audit metadata for important commands and state changes.

Owns:

- Audit records.

Each audit record must include enough information to understand:

- When the change occurred.
- Who performed the change.
- What operation was performed.
- What resource was affected.
- The meaningful before and after state, where applicable.

The Audit boundary is an architectural concern. It should not introduce additional domain concepts into the core budgeting model.

### 7.6 Web Application

Responsibilities:

- User interface.
- Authentication flow.
- Budget creation and management.
- Budget item management.
- Transaction entry.
- Transaction allocation workflow.
- Budget Summary views.
- Reporting views.
- Audit timeline views, where implemented.

The frontend should use API responses shaped for user workflows rather than exposing internal aggregate structure directly.

### 7.7 Logical Architecture Diagram

```text
                    +------------------+
                    |   Web Frontend   |
                    +--------+---------+
                             |
                             v
                    +------------------+
                    |     HTTP API     |
                    +--------+---------+
                             |
          +------------------+------------------+
          |                  |                  |
          v                  v                  v
   +-------------+    +--------------+    +-------------+
   | Budgeting   |    | Transactions |    | Reporting   |
   | Boundary    |    | Boundary     |    | Boundary    |
   +------+------+    +------+-------+    +------+------+ 
          |                  |                  |
          v                  v                  v
   +-------------+    +--------------+    +-------------+
   | Budget Data |    | Transaction  |    | Reports     |
   |             |    | Data         |    |             |
   +-------------+    +--------------+    +-------------+
                             |
                             v
                       +-----------+
                       |   Audit   |
                       | Boundary  |
                       +-----------+
```

The diagram describes logical ownership. It does not require separate processes, separate databases, or asynchronous integration.

## 8. Application Architecture

The application should use Vertical Slice Architecture as its primary application structure.

Vertical Slice Architecture organises code around user-facing features and use cases rather than around horizontal technical layers. Each slice should contain the application logic, validation, request and response models, persistence interaction, and tests needed to support that feature.

The purpose of this structure is to make features easier to understand, change, test, and review independently.

BudgetyTzar is currently simple enough that a less structured approach could also work. Vertical Slice Architecture is still preferred because it is a lightweight, well-understood pattern that supports future growth without requiring a major architectural refactor.

The application should use lightweight domain-driven design principles within slices where they improve the model. This means:

- Use ubiquitous language consistently.
- Make business invariants explicit.
- Prefer domain concepts over primitive data structures where doing so improves correctness.
- Keep illegal states difficult to represent.
- Keep domain behaviour close to the concepts that own the rules.

The application does not require a full tactical domain-driven design implementation. Aggregates, value objects, domain services, repositories, events, and other domain-driven design patterns should be introduced only where they solve a clear problem in the current model.

Vertical slices and logical boundaries are complementary. Vertical slices organise feature implementation. Logical boundaries define ownership of data, rules, and language. A slice may coordinate across boundaries where required by a use case, but it must not blur ownership between boundaries.

## 9. Data Storage

Recommended storage:

- PostgreSQL for operational data.

Storage rules:

- Monetary values must use decimal-compatible database types.
- Floating point types must not be used for monetary values.
- The money scale is two decimal places.
- Stored input monetary amounts, such as budget item planned amounts and transaction amounts, must support values from `0.00` to `99999999.99`.
- Budget item planned amounts and transaction amounts must be greater than `0.00`.
- Derived values, including actual amounts, remaining amounts, and reporting totals, may be zero or negative where permitted by the calculation rules.
- The model does not require rounding rules because reporting calculations only sum stored monetary values.
- Logical boundaries should have clear table ownership.
- Cross-boundary writes should be avoided.
- Transactions are stored independently of budgets.
- Transaction allocations store the association between a transaction and a budget item.

## 10. APIs

APIs should be HTTP/JSON for user-driven commands and queries.

The routes below define the supported public API contract for the application.

### 10.1 Budgeting API

```http
POST   /api/budgets
GET    /api/budgets
GET    /api/budgets/{budgetId}
PUT    /api/budgets/{budgetId}/name
POST   /api/budgets/{budgetId}/budget-items
GET    /api/budgets/{budgetId}/budget-items
GET    /api/budgets/{budgetId}/budget-items/{budgetItemId}
PUT    /api/budgets/{budgetId}/budget-items/{budgetItemId}/name
PUT    /api/budgets/{budgetId}/budget-items/{budgetItemId}/planned-amount
DELETE /api/budgets/{budgetId}/budget-items/{budgetItemId}
```

The Budgeting API owns budget and budget item commands.

Budget and budget item updates are represented as specific `PUT` operations so that the API does not imply unsupported generic mutation. In particular, there is no endpoint for changing a budget item's kind after creation.

Example rename budget request:

```json
{
  "name": "UK 2026"
}
```

Example change budget item planned amount request:

```json
{
  "plannedAmount": "450.00"
}
```

The Budgeting API should not expose transaction entry as a child operation of a budget, because transactions are independent of budgets.

### 10.2 Transaction API

```http
POST /api/transactions
GET  /api/transactions
GET  /api/transactions/{transactionId}
GET  /api/transactions?from={date}&to={date}&allocationStatus={allocationStatus}
DELETE /api/transactions/{transactionId}
```

The Transaction API owns transaction recording and transaction retrieval.

Transactions may exist without being allocated to a budget item.

The `allocationStatus` query parameter supports `allocated`, `unallocated`, and `all`.

Deleting a transaction must be rejected while the transaction is allocated to a budget item.

### 10.3 Transaction Allocation API

```http
PUT    /api/transactions/{transactionId}/allocation
GET    /api/transactions/{transactionId}/allocation
DELETE /api/transactions/{transactionId}/allocation
```

An allocation request identifies the budget item to allocate the transaction to.

Example allocation request:

```json
{
  "budgetItemId": "budget-item-guid"
}
```

The system must validate that:

- The transaction exists.
- The budget item exists.
- The transaction is not already allocated to another budget item.
- The transaction currency matches the currency of the budget that owns the budget item.

The allocation applies the full transaction amount.

If the transaction is not allocated, `PUT /api/transactions/{transactionId}/allocation` creates the allocation and returns `200 OK`.

If the transaction is already allocated to the requested budget item, `PUT /api/transactions/{transactionId}/allocation` returns `200 OK` and leaves state unchanged.

If the transaction is already allocated to a different budget item, `PUT /api/transactions/{transactionId}/allocation` must reject the request.

### 10.4 Reporting API

```http
GET /api/budgets/{budgetId}/summary
```

The Budget Summary is the primary reporting view.

### 10.5 Example Budget Summary Response

```json
{
  "budgetId": "budget-guid",
  "name": "UK",
  "currency": "GBP",
  "funding": {
    "items": [
      {
        "budgetItemId": "budget-item-guid-1",
        "name": "Salary",
        "plannedAmount": "3000.00",
        "actualAmount": "3000.00",
        "remainingAmount": "0.00"
      }
    ],
    "totalPlannedAmount": "3000.00",
    "totalActualAmount": "3000.00",
    "totalRemainingAmount": "0.00"
  },
  "consumption": {
    "items": [
      {
        "budgetItemId": "budget-item-guid-2",
        "name": "Groceries",
        "plannedAmount": "400.00",
        "actualAmount": "250.00",
        "remainingAmount": "150.00"
      }
    ],
    "totalPlannedAmount": "400.00",
    "totalActualAmount": "250.00",
    "totalRemainingAmount": "150.00"
  },
  "overall": {
    "plannedSurplus": "2600.00",
    "actualSurplus": "2750.00"
  }
}
```

## 11. User Interface

### 11.1 Main Views

- Budget list.
- Budget detail.
- Budget item management.
- Budget Summary.
- Transaction list.
- Transaction entry.
- Transaction allocation workflow.
- Reports.
- Audit timeline, where implemented.

### 11.2 Budget Detail

The Budget Detail view should show:

- Budget name.
- Budget currency.
- Budget items.
- Budget item kind.
- Budget item planned amount.
- Access to the Budget Summary.

The UI should present funding and consumption clearly and separately where doing so improves understanding.

### 11.3 Budget Summary

The Budget Summary should show:

- Budget name.
- Budget currency.
- Funding items.
- Consumption items.
- Planned amount for each item.
- Actual amount for each item.
- Remaining amount for each item.
- Total planned funding.
- Total actual funding.
- Total remaining funding.
- Total planned consumption.
- Total actual consumption.
- Total remaining consumption.
- Planned surplus.
- Actual surplus.

Funding and consumption must be presented separately.

Negative remaining amounts should be visible and understandable:

- Negative remaining funding means more funding was received than planned.
- Negative remaining consumption means spending exceeded the planned amount.

### 11.4 Transaction Allocation Workflow

The allocation workflow should show:

- Unallocated transactions.
- Existing allocation, where a transaction is already allocated.
- Candidate budget items.
- Transaction currency.
- Budget item budget currency.
- Allocation action.
- Allocation removal action.

The allocation workflow must make it clear that:

- A transaction can be allocated to at most one budget item.
- An allocation applies the full transaction amount.
- Transaction type and budget item kind are different concepts.

### 11.5 Reports

Reports should include:

- Budget Summary.
- Budget versus actual funding.
- Budget versus actual consumption.
- Activity by budget item.
- Allocation status.
- Unallocated transactions.

Reports should use the same terms as the domain model.

## 12. Domain Invariants and Business Rule Summary

This section summarises the rules defined throughout the functional requirements. If there is a conflict, the more detailed functional requirement should be treated as authoritative.

- Monetary values must use decimal types, never floating point.
- Monetary values use two decimal places.
- Stored input monetary amounts must be within the range `0.00` to `99999999.99`.
- Each budget has one currency.
- A budget may exist without budget items.
- A budget does not have a date range.
- All planned amounts within a budget use the budget currency.
- Budget item planned amounts must be positive.
- Transaction amounts must be positive.
- A transaction's date must not restrict whether the transaction can be allocated to a budget item.
- Derived actual amounts may be negative.
- Derived remaining amounts may be negative.
- A budget item kind must be either `Funding` or `Consumption`.
- A budget item kind must not change after the budget item has been created.
- Refunds, corrections, reversals, underpayments, and overpayments must not change the kind of a budget item.
- Total planned funding must be greater than or equal to total planned consumption.
- The system must reject budget item changes that would make total planned consumption exceed total planned funding.
- A transaction type must be either `Credit` or `Debit`.
- Transaction type records the direction of real-world financial activity.
- Transaction type does not determine whether a transaction is funding or consumption.
- Transactions are independent of budgets.
- A transaction may exist without being allocated to a budget item.
- A transaction must not be deleted while it is allocated to a budget item.
- A deleted transaction must not affect budget actuals, remaining amounts, or Budget Summary totals.
- Unallocated transactions must not affect budget actuals, remaining amounts, or Budget Summary totals.
- A transaction may be allocated to at most one budget item.
- An allocation applies the full transaction amount to the selected budget item.
- Allocating a transaction to the same budget item it is already allocated to must succeed without changing state.
- Allocating a transaction to a different budget item while it already has an allocation must be rejected.
- A transaction must not be allocated to a budget item that does not exist.
- A transaction must not be allocated to a budget item if the transaction currency does not match the budget currency.
- A budget item must not be deleted while any transaction is allocated to it.
- For `Funding` items, `Credit` transactions increase actual funding.
- For `Funding` items, `Debit` transactions decrease actual funding.
- For `Consumption` items, `Debit` transactions increase actual consumption.
- For `Consumption` items, `Credit` transactions decrease actual consumption.
- Remaining amount is calculated as planned amount minus actual amount.
- Actual amounts may exceed planned amounts.
- Remaining amounts may be negative.

## 13. Product Versioning

BudgetyTzar should use one product-wide semantic version for the repository and released application, following SemVer 2.0.0:

```text
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]
```

Version rules:

- Increment `MAJOR` for incompatible public HTTP API behaviour or release packaging changes.
- Increment `MINOR` for backward-compatible functionality.
- Increment `PATCH` for backward-compatible fixes.
- Use Conventional Commits to describe release intent: `feat` maps to `MINOR`, `fix` and `perf` map to `PATCH`, and `!` or a `BREAKING CHANGE:` footer maps to `MAJOR` once the product reaches `1.0.0`.
- Commit types `refactor`, `docs`, `test`, `build`, `ci`, `chore`, `style`, and `revert` do not imply a product version bump unless they include breaking-change notation.
- Pre-`1.0.0` releases may evolve faster, but breaking changes must still be documented.
- Database migrations do not automatically require a major version unless they remove or change supported behaviour incompatibly.

Release requirements:

- Git release tags should use SemVer tags such as `v0.1.0`, `v0.2.0`, and `v1.0.0`; these tags are the canonical released versions.
- Builds should generate version metadata from the latest reachable tag and subsequent Conventional Commits. Tagged commits expose the tag version; commits after a tag expose deterministic preview metadata.
- The runtime API should expose product version metadata separately from health status.
- OpenAPI metadata should include the product SemVer.
- Container image tags should include explicit SemVer tags such as `budgetytzar-api:0.2.0`; `latest` may exist only as a convenience tag and must not be the release identity.
- GitHub Releases should be the human changelog and release-notes surface.
- Generated version metadata and generated release-note files should be excluded from source control.
- Local development should provide a versioned commit-message hook that validates Conventional Commits without requiring Node tooling.

## 14. Technology Choices

### 14.1 Backend Stack

- .NET current LTS or agreed project version.
- ASP.NET Core Web API.
- Entity Framework Core.
- PostgreSQL provider.
- FluentValidation or equivalent validation approach.
- xUnit or NUnit.
- Testcontainers.
- OpenTelemetry.

### 14.2 Frontend Stack

- TypeScript.
- React, Next.js, Blazor, or another agreed frontend framework.
- TanStack Query or equivalent server-state library, if using React.
- A charting library such as Recharts, if richer reporting charts are implemented.
- Playwright for end-to-end tests.

### 14.3 Infrastructure

- Docker.
- Docker Compose.
- GitHub Actions.
- PostgreSQL.
- OpenTelemetry Collector.
- Prometheus and Grafana.

## 15. Development Process

Development should proceed incrementally using feature slices or smaller reviewable changes.

Each increment should be small enough to understand, test, and review independently. A feature slice may be split into smaller increments where that reduces risk or improves clarity.

Development should follow test-driven development where practical:

1. Define the externally observable behaviour required by the increment.
2. Review the relevant domain, API, observability, security, privacy, audit, and operational requirements before implementation begins.
3. Add or update failing tests that describe the required behaviour.
4. Implement the smallest change needed to make the tests pass.
5. Refactor while keeping the tests passing.
6. Update this specification where the increment clarifies, changes, or adds product, domain, API, observability, security, privacy, audit, or operational requirements.

Before implementing each feature slice or smaller increment, the developer must explicitly review whether the increment requires changes to:

- Domain rules and invariants.
- API behaviour and response shapes.
- Validation and error handling.
- Authorisation and ownership rules.
- Audit requirements.
- Structured logging.
- Metrics.
- Distributed tracing.
- Health checks.
- Sensitive data handling.
- Privacy requirements.
- Documentation.

Observability and security requirements must be considered at the beginning of each increment, not after implementation. If the increment introduces new endpoints, commands, queries, failure modes, background processing, cross-boundary dependencies, sensitive data handling, or operationally important behaviour, the relevant observability, security, privacy, and audit requirements must be refined in this specification as part of the same increment.

The result of this review may be that no specification change is required. That decision should be deliberate rather than accidental.

Implementation should not introduce architectural patterns, abstractions, infrastructure, observability mechanisms, or security mechanisms that are not justified by the current increment or by an explicit requirement in this specification.

## 16. Testing Strategy

The testing strategy should protect refactoring by making supported user-facing behaviour observable through automated tests. The most important regression protection should come from tests that exercise supported use cases through the public HTTP API.

Tests should verify externally observable application behaviour rather than implementation details. For product behaviour, tests should drive the application through API requests and assert against API responses, including status codes, validation errors, resource representations, and reporting results. They should not verify behaviour by directly inspecting backend SQL state.

Database-level assertions are appropriate only for persistence-specific concerns such as schema shape, migration behaviour, decimal precision, provider-specific query behaviour, indexes, or constraints that cannot be adequately verified through the public API.

### 16.1 API Behaviour Tests

API behaviour tests are the primary automated test suite for domain use cases and regression protection.

These tests should exercise the application through HTTP/JSON APIs and verify behaviour through public API responses. They should be able to run against a realistic application composition, including PostgreSQL via Testcontainers where practical.

API behaviour tests should cover:

- Budget creation, retrieval, and renaming.
- Budget creation without budget items.
- Budget item creation, retrieval, renaming, planned amount changes, deletion, and validation.
- Rejection of budget item changes that would make total planned consumption exceed total planned funding.
- Rejection of attempts to change a budget item's kind after creation.
- Rejection of budget item deletion while transactions are allocated to it.
- Transaction recording and retrieval.
- Transaction deletion.
- Rejection of transaction deletion while the transaction is allocated to a budget item.
- Transaction allocation and allocation removal.
- Allocation of a transaction to a budget item regardless of the transaction date.
- Rejection of allocation to a missing budget item.
- Rejection of allocation when the transaction currency does not match the budget currency.
- Successful idempotent allocation when a transaction is already allocated to the requested budget item.
- Rejection of allocation to a different budget item when the transaction is already allocated.
- Full-amount allocation semantics.
- Unallocated transactions not affecting budget actuals, remaining amounts, or Budget Summary totals.
- Deleted transactions not affecting budget actuals, remaining amounts, or Budget Summary totals.
- Actual amount calculation for funding items.
- Actual amount calculation for consumption items.
- Remaining amount calculation.
- Negative actual and remaining amount scenarios.
- Monetary scale and range validation.
- Budget Summary response shape and calculations.
- Runtime version endpoint and OpenAPI version metadata.

API behaviour tests should be written at the level of use cases and business rules. They should remain stable across internal refactoring as long as the public API behaviour remains stable.

### 16.2 Unit Tests

Unit tests are supplementary. They should be used where they provide clearer, faster, or more focused feedback than API behaviour tests.

Unit tests are appropriate for:

- Pure domain calculations.
- Value object behaviour.
- Business rules that can be expressed without infrastructure.
- Edge cases that would be awkward or overly verbose to exercise through the API.
- Complex mapping or calculation logic where a focused test improves maintainability.

Unit tests should not become the main specification of user-facing behaviour when the same behaviour is better expressed through the public API.

### 16.3 PostgreSQL Integration Tests

PostgreSQL integration tests should cover persistence-specific behaviour that cannot be adequately protected by API behaviour tests alone.

Cover:

- Schema and migration behaviour.
- Decimal precision for monetary values.
- Provider-specific query behaviour.
- Indexes and constraints where relevant.
- Persistence behaviour that differs materially from SQLite or in-memory stores.

Use Testcontainers where practical.

SQLite or in-memory tests may be used for fast feedback, but they do not replace PostgreSQL integration coverage for persistence requirements.

### 16.4 Contract Tests

Cover:

- Public API contracts between frontend and backend.
- Reporting API response contracts consumed by the frontend.
- Backward-compatible API evolution.

### 16.5 End-to-End Tests

End-to-end tests should cover representative user workflows through the UI and API together. They should be fewer in number than API behaviour tests and should focus on confidence that the application works as a whole.

Cover:

- Create a budget.
- Create funding and consumption budget items.
- Record a credit transaction.
- Record a debit transaction.
- Allocate a credit transaction to a funding item.
- Allocate a debit transaction to a consumption item.
- Allocate a credit transaction to a consumption item as a refund.
- Allocate a debit transaction to a funding item as a correction.
- Remove a transaction allocation.
- Delete a transaction.
- View the Budget Summary.
- View unallocated transactions.
- Validate that a budget item with allocations cannot be deleted.

## 17. Observability

The application should include:

- Structured logging.
- Correlation IDs.
- Distributed tracing with OpenTelemetry.
- Metrics for API requests.
- Health endpoints implemented using standard .NET health check infrastructure.
- Dashboard for service health.

Important signals:

- Error rate for each endpoint.
- Latency for each endpoint, including `p90`, `p95`, and `p99`.
- Validation failure rate.
- Transaction allocation failure rate.
- Budget Summary query latency.
- Database connection and query health.

## 18. Security and Privacy

- Use HTTPS in deployed environments.
- Protect APIs with authentication.
- Enforce domain resource authorisation using the authenticated identity supplied by the Identity boundary.
- Store secrets outside source control.
- Avoid logging full bank transaction descriptions if they may contain sensitive information.
- Avoid logging unnecessary financial details.
- Support data export.
- Support full data deletion for the user.

## 19. Deployment

### 19.1 Local Development

Use Docker Compose for:

- PostgreSQL.
- Backend application.
- Frontend.
- Observability dependencies, where useful.

### 19.2 Containerised Deployment

The application should provide container images for deployable components.

Container image tags should include explicit SemVer tags such as `budgetytzar-api:0.2.0`; `latest` may exist only as a convenience tag and must not be the release identity.

## 20. Documentation and Repository Expectations

The repository should include:

- Clear README with the product purpose and architecture overview.
- Local quick-start instructions.
- API documentation.
- Explanation of the domain model.
- Explanation of the logical boundaries.
- Deployment guide, where deployment automation exists.
- CI/CD workflow documentation.
- SemVer release tags.
- Changelog or release notes grouped by SemVer.
- Visible runtime version metadata.
- API behaviour test coverage summary.
- Supplementary unit, integration, contract, and end-to-end test coverage summary.
- Observability documentation.

Suggested project narrative:

> BudgetyTzar replaces an error-prone spreadsheet budgeting workflow with a maintainable budgeting application that separates financial planning from real-world transaction activity. It models budgets as financial plans, transactions as independent financial facts, and allocations as the link between the two. The architecture uses clear domain boundaries, SQL-backed persistence, reporting views, strong automated testing, observability, and deployment discipline.
