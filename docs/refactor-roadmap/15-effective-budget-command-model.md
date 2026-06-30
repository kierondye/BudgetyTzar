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
- Move toward immutable domain behaviour: command methods should preferably return modified domain objects instead of mutating existing instances.
- Do not complete immutability by making a broad, risky rewrite. Make it incremental and document remaining mutable areas.
- Avoid broad renaming unless it directly supports the new model.
- Transactions are out of scope for this step.

### Minimise Domain Concepts

Prefer enriching existing domain objects and established architectural abstractions over introducing new types.

Do not introduce new classes simply because behaviour needs somewhere to live.

Every new type should represent either:

- a concept from the ubiquitous language, or
- a widely recognised architectural pattern.

When in doubt, prefer extending an existing repository, domain object, or value object before introducing a new service.

## Design Direction

`EffectiveBudget` should model the budget's effective command state as of a date:

- Budget id.
- Effective date.
- Budget items available to the command.
- Point-in-time planned amounts and balances.
- Pending modifications produced during the unit of work.
- Pending domain events produced by successful domain operations.

Callers should express intent through `EffectiveBudget` methods such as `RecordAdjustment(...)` rather than navigating to child objects and asking those objects to create changes. Any `EffectiveBudgetItem` representation should be private or internal implementation detail, not the public command surface.

Vertical slice handlers may inspect `EffectiveBudgetResult` cases, but they should not know the persistence anatomy of a successful `EffectiveBudget`. Saving pending adjustments, events, and any future pending records belongs behind a single persistence boundary.

## Immutability Direction

The domain model should move toward immutable domain logic.

For this step, treat immutability as an architectural direction, not a requirement to rewrite the whole model in one increment.

Guidance:

- Prefer domain methods that return a new modified domain object rather than mutating the existing instance.
- Avoid exposing mutable collections from domain objects.
- Avoid setters on domain state where practical.
- Prefer constructors/factories that produce valid objects.
- Pending changes should be represented as part of the returned `EffectiveBudget`, not by mutating hidden internal lists where this can be changed safely.
- If full immutability would make an increment too large, preserve behaviour and document the remaining mutable state as follow-up work.
- Do not introduce broad copy/rebuild infrastructure just to achieve immutability in one pass.
- Keep EF persistence compatibility in mind, but do not let EF shape the public domain API unnecessarily.

Preferred direction:

```csharp
var result = effectiveBudget.RecordAdjustment(...);

if (result is EffectiveBudgetResult.Success success)
{
    var modifiedBudget = success.Budget;
    effectiveBudgetRepository.Save(modifiedBudget);
}
```

Rather than relying on mutation of the original instance:

```csharp
effectiveBudget.RecordAdjustment(...);
effectiveBudgetRepository.Save(effectiveBudget);
```

The success result should contain the modified `EffectiveBudget`.

### Architectural Naming

When introducing new types, prefer established DDD, OO, and .NET architectural patterns over project-specific names.

Before introducing a new service or infrastructure type, first determine whether an existing architectural pattern already describes its responsibility.

Examples include:

- Repository
- Factory
- Domain Service
- Specification
- Policy
- Mapper

Avoid introducing new classes with names such as:

- `*Saver`
- `*Manager`
- `*Processor`
- `*Helper`
- `*Wrapper`
- `*Context`

unless they represent a genuine domain concept or a well-established architectural pattern.

A new type should exist because it represents a recognised concept, not simply because code needs somewhere to live.

## Proposed Increments

### Increment 1 - Record Adjustment Through Effective Budget

Status: implemented, awaiting review.

- Add `EffectiveBudget.RecordAdjustment(budgetItemId, amount, type, notes)`.
- Introduce a closed result type for effective budget modifications, with cases for success, item not found, archived item, and validation failure.
- `EffectiveBudgetResult.Success(EffectiveBudget Budget)` should return the modified effective budget. It should not require callers to assume the original instance was mutated.
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

### Increment 2 - Introduce Effective Budget Save Boundary

Status: implemented, awaiting review.

Goal: move knowledge of how to persist an `EffectiveBudget` out of vertical slice handlers and into a single persistence boundary.

- The persistence boundary should preferably be represented by an existing repository abstraction.
- Preferred direction:

```csharp
effectiveBudgetRepository.Save(effectiveBudget);
```

- If returning a persistence result is justified:

```csharp
var result = effectiveBudgetRepository.Save(effectiveBudget);
```

- Avoid introducing service types such as:

```text
EffectiveBudgetSaver
BudgetSaver
BudgetPersistenceManager
```

unless the roadmap later identifies a genuine architectural need for them.

- If the existing repository abstraction cannot naturally support saving an `EffectiveBudget`, prefer extending or introducing an `IEffectiveBudgetRepository` rather than creating a dedicated `*Saver` service.
- The adjustment handler should no longer directly persist individual pending collections such as:
  - `budget.PendingAdjustments.Single()`.
  - `budget.PendingEvents.Single()`.
- The handler should only coordinate the use case:
  - load the effective budget.
  - call `RecordAdjustment(...)`.
  - handle failure result cases.
  - save the successful effective budget through the persistence boundary.
- The save boundary should persist all pending adjustments and pending events currently owned by `EffectiveBudget`.
- Use `AddRange(...)` rather than assuming there is exactly one pending adjustment or event.
- Keep EF-specific persistence details outside the domain model.
- Do not put `Save(...)` methods on `EffectiveBudget` itself.
- Do not introduce a broad Unit of Work abstraction unless the existing code already has one.
- Add focused tests if there is existing coverage around the handler/persistence path.

Rationale:

`EffectiveBudget` is now the temporal command model. The vertical slice handler should not know the exact list of pending child records that must be persisted after a successful command. If a later operation adds pending reallocations, audit records, snapshots, or multiple events, the handler should not need to change. That knowledge belongs at the persistence boundary.

Implementation notes:

- Added `IEffectiveBudgetRepository.Save(EffectiveBudget, CancellationToken)` as the persistence boundary for successful effective budget command models.
- Implemented `EffectiveBudgetRepository` using EF-backed persistence and the existing outbox writer.
- The repository persists all pending adjustments with `AddRange(...)`.
- The repository writes all pending domain events owned by the effective budget.
- The adjustment handler now coordinates the use case and saves the successful effective budget through the repository boundary.
- The adjustment handler no longer directly persists `PendingAdjustments` or `PendingEvents`.
- The save result returns the first created adjustment and first event id to preserve the existing HTTP response body and projection header shape while still saving all pending records.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 14 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 111 tests.

### Increment 3 - Hide Effective Budget Item Details

Status: next.

- Remove public item lookup flows if no remaining callers depend on them.
- Make any item state wrapper private or internal if still useful.
- Keep `EffectiveBudgetItemState` as an assembly-facing hydration input only if it remains the simplest repository/application boundary.

### Increment 4 - Strengthen Money Input

Status: deferred.

- Add a non-throwing money factory/result for user-input validation.
- Move command handlers to convert raw decimal input into a valid positive money value before calling domain operations.
- Avoid relying on `MoneyAmount.Positive(...)` throwing inside expected validation paths.
- Keep API validation messages stable unless explicitly changing validation contracts.

### Increment 5 - Let Budget Own Budget Items

Status: deferred.

- Move from `Budget.CreateBudgetItem(existingItems, ...)` toward `Budget` owning its item collection.
- Keep duplicate budget-item-name rules inside `Budget`.
- Keep `BudgetItemKind` rather than introducing `FundingBudgetItem` and `ConsumptionBudgetItem` subclasses unless later code clearly justifies polymorphism.
- Keep budget items simple: identity, name, kind, archive state.

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
