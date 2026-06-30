# 15 - Effective Budget Command Model

## Goal

Refactor the budgeting domain model toward a stricter DDD/OO shape by treating `EffectiveBudget` as the temporal command model for budget modifications at a specific date. It should represent the effective budget as of a date, own point-in-time validation, collect pending changes and domain events, and become the public behavioral entry point for modifications that depend on calculated budget state.

This is an incremental refactor. Preserve current API, persistence, event payload, and projection behavior unless a later increment explicitly changes them.

## Constraints

- Keep changes small, reviewable, and compatible with the current EF-backed command model.
- Do not redesign the whole budgeting model in one pass.
- Do not introduce event sourcing infrastructure, aggregate base classes, service locators, or domain data access.
- Do not move historical adjustments, transactions, or reporting read models into `Budget`.
- Preserve existing event names and payloads unless a later increment has a clear contract reason to change them.
- Prefer result objects for expected domain rejections. Exceptions remain acceptable for programmer errors or impossible construction paths.
- Avoid broad renaming unless it directly supports the new model.
- Transactions are out of scope for this step.

## Design Direction

`EffectiveBudget` should model the budget's effective command state as of a date:

- Budget id.
- Effective date.
- Budget items available to the command.
- Point-in-time planned amounts and balances.
- Pending modifications produced during the unit of work.
- Pending domain events produced by successful domain operations.

Callers should express intent through `EffectiveBudget` methods such as `RecordAdjustment(...)` rather than navigating to child objects and asking those objects to create changes. Any `EffectiveBudgetItem` representation should be private or internal implementation detail, not the public command surface.

## Proposed Increments

### Increment 1 - Record Adjustment Through Effective Budget

Status: implemented, awaiting review.

- Add `EffectiveBudget.RecordAdjustment(budgetItemId, amount, type, notes)`.
- Introduce a closed result type for effective budget modifications, with cases for success, item not found, archived item, and validation failure.
- Move adjustment validation currently reached through `EffectiveBudgetItem.CreateAdjustment(...)` into `EffectiveBudget.RecordAdjustment(...)`.
- Let successful operations add the adjustment and domain event to pending collections owned by `EffectiveBudget`.
- Update in-memory effective planned amounts after success so subsequent operations in the same unit of work validate against the new state.
- Update the budget adjustment handler to call `EffectiveBudget.RecordAdjustment(...)` and persist pending changes from the effective budget.
- Add or update focused domain tests for the new public command method.

Implementation notes:

- Added `EffectiveBudget.RecordAdjustment(...)` as the public behavior for recording point-in-time budget adjustments.
- Replaced the public lookup-and-child-call flow with a closed `EffectiveBudgetResult` hierarchy.
- Kept item state as a private implementation detail of `EffectiveBudget`.
- Successful adjustments are now held in `EffectiveBudget.PendingAdjustments`.
- Successful adjustment domain events are now held in `EffectiveBudget.PendingEvents`.
- The adjustment handler persists the pending adjustment and writes the pending event from the effective budget.
- The in-memory net planned amount and item planned amount are updated after success so later operations on the same effective budget validate against the modified position.
- Existing event type and payload behavior were preserved.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 13 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 110 tests.

### Increment 2 - Hide Effective Budget Item Details

Status: deferred.

- Remove public item lookup flows if no remaining callers depend on them.
- Make any item state wrapper private or internal if still useful.
- Keep `EffectiveBudgetItemState` as an assembly-facing hydration input only if it remains the simplest repository/application boundary.

### Increment 3 - Strengthen Money Input

Status: deferred.

- Add a non-throwing money factory/result for user-input validation.
- Move command handlers to convert raw decimal input into a valid positive money value before calling domain operations.
- Avoid relying on `MoneyAmount.Positive(...)` throwing inside expected validation paths.
- Keep API validation messages stable unless explicitly changing validation contracts.

### Increment 4 - Let Budget Own Budget Items

Status: deferred.

- Move from `Budget.CreateBudgetItem(existingItems, ...)` toward `Budget` owning its item collection.
- Keep duplicate budget-item-name rules inside `Budget`.
- Keep `BudgetItemKind` rather than introducing `FundingBudgetItem` and `ConsumptionBudgetItem` subclasses unless later code clearly justifies polymorphism.
- Keep budget items simple: identity, name, kind, archive state.

### Increment 5 - Repository/Application Save Shape

Status: deferred.

- Make repository or handler save paths consume pending changes and events from `EffectiveBudget`.
- Keep EF persistence orchestration in infrastructure/application code.
- Consider a repository abstraction only if it reduces real duplication or clarifies aggregate persistence without hiding important EF behavior.

## Validation Rules

`EffectiveBudget.RecordAdjustment(...)` should validate:

- The budget item exists in the effective budget.
- Archived items cannot accept activity after their archive date.
- Net planned spending must not exceed net planned income.
- Consumption items must not become funding sources through budget adjustments.
- Funding items must not become consumption items through budget adjustments.
- Amounts must represent valid positive money before an adjustment is recorded.

Expected domain rejections should be returned as effective budget result cases, not thrown exceptions.

## Tests

Increment 1 should include focused domain coverage for:

- Item not found.
- Archived item.
- Invalid overall net planned position.
- Invalid item kind position for consumption items.
- Invalid item kind position for funding items.
- Successful adjustment records a pending adjustment.
- Successful adjustment records a pending domain event with the existing event payload behavior.
- Successful adjustment updates the in-memory effective planned amount for later operations in the same unit of work.

Focused command/API tests should be adjusted only where needed to prove behavior remains stable.

## Explicit Out Of Scope

- Event sourcing infrastructure.
- Event-store-backed repositories or rehydration.
- Transaction aggregate-boundary changes.
- `FinancialTransaction` refactors.
- `TransactionAllocation` refactors.
- Moving transactions into `Budget`.
- Loading all historical adjustments into `Budget`.
- Reporting read-model redesign.
- Projection contract changes.
- New service locator or mediator abstractions.
- Polymorphic budget item subclasses.
