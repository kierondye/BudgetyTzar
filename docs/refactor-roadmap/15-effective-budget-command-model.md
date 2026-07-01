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

### Step 15 Completion Targets

The earlier increments moved the command flow in the right direction, but Step 15 is not complete until the budgeting command-domain types involved in effective budget modification no longer expose mutable state as their public shape.

Complete this step by making these types immutable in focused increments:

- `Budget`
- `BudgetAdjustment`
- `BudgetItem`
- `BudgetReallocation`
- `EffectiveBudget`

Also complete the persistence boundary direction by moving domain model population out of vertical slice handlers. Handlers may request the command model they need, such as `EffectiveBudget` or `Budget`, but query shape, EF projection, child collection loading, and hydration details should live behind data access abstractions.

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

Status: implemented, awaiting review.

- Remove public item lookup flows if no remaining callers depend on them.
- Make any item state wrapper private or internal if still useful.
- Keep `EffectiveBudgetItemState` as an assembly-facing hydration input only if it remains the simplest repository/application boundary.

Implementation notes:

- Confirmed there are no remaining public item lookup flows on `EffectiveBudget`.
- Kept the nested `EffectiveBudgetItem` private.
- Made `EffectiveBudgetItemState` internal so it remains an assembly-facing hydration input rather than public command surface.
- Made the `EffectiveBudget` hydration constructor internal because it depends on the internal hydration input.
- Added test assembly internals access for focused domain tests without exposing the hydration shape publicly.
- Added a focused public surface test to assert `EffectiveBudgetItemState` is not exported.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 15 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 112 tests.

### Increment 4 - Strengthen Money Input

Status: implemented, awaiting review.

- Add a non-throwing money factory/result for user-input validation.
- Move command handlers to convert raw decimal input into a valid positive money value before calling domain operations.
- Avoid relying on `MoneyAmount.Positive(...)` throwing inside expected validation paths.
- Keep API validation messages stable unless explicitly changing validation contracts.

Implementation notes:

- Added `PositiveMoneyAmount.Create(...)` with a closed `PositiveMoneyAmountResult` for expected positive-money validation failures.
- Preserved existing `MoneyAmount.Positive(...)` throwing behavior for programmer-error construction paths.
- Moved the adjustment handler to convert the raw decimal amount into `PositiveMoneyAmount` before calling `EffectiveBudget.RecordAdjustment(...)`.
- Added a `PositiveMoneyAmount` overload for `EffectiveBudget.RecordAdjustment(...)` and kept the decimal overload as a compatibility shim that returns validation failures instead of throwing.
- Added an internal `PositiveMoneyAmount` overload for `BudgetAdjustment.Create(...)` so successful effective budget commands can create adjustments without revalidating through the throwing factory.
- Preserved existing validation messages and event payload behavior.
- Kept transactions, reallocations, and broader money input refactors out of scope for this increment.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~MoneyAmountTests|FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 20 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 117 tests.

### Increment 5 - Let Budget Own Budget Items

Status: implemented, awaiting review.

- Move from `Budget.CreateBudgetItem(existingItems, ...)` toward `Budget` owning its item collection.
- Keep duplicate budget-item-name rules inside `Budget`.
- Keep `BudgetItemKind` rather than introducing `FundingBudgetItem` and `ConsumptionBudgetItem` subclasses unless later code clearly justifies polymorphism.
- Keep budget items simple: identity, name, kind, archive state.

Implementation notes:

- Added a budget-owned item collection exposed as a read-only `Budget.Items` view.
- Replaced the public `Budget.CreateBudgetItem(existingItems, ...)` flow with `Budget.CreateBudgetItem(name, kind)`.
- Moved duplicate-name validation to use the budget-owned item collection.
- Added internal budget-item hydration for the EF-backed command path so the domain owns the collection without introducing a schema or migration change in this increment.
- Kept `BudgetItem` simple and preserved `BudgetItemKind`.
- Kept transaction, allocation, reallocation, and effective-budget behavior out of scope.
- Preserved existing budget-item event names and payloads.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests"` - passed, 19 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 118 tests.

### Increment 6 - Return Modified Effective Budget

Status: implemented, awaiting review.

- Move `EffectiveBudget.RecordAdjustment(...)` closer to the immutability direction by returning a modified successful `EffectiveBudget` instead of mutating the original instance.
- Keep the existing `EffectiveBudgetResult.Success(EffectiveBudget Budget)` shape.
- Preserve the existing persistence boundary and handler flow.
- Keep failure cases non-mutating.
- Do not broaden the change into a full domain immutability rewrite.

Implementation notes:

- `EffectiveBudget` now stores planned state, pending adjustments, and pending events as constructor-provided read-only collections.
- Successful `RecordAdjustment(...)` creates and returns a new `EffectiveBudget` with updated net planned amount, item planned amount, pending adjustment, and pending event.
- The original `EffectiveBudget` remains unchanged after a successful command.
- Sequential commands now explicitly chain through `EffectiveBudgetResult.Success.Budget`.
- The existing repository save boundary continues to persist pending adjustments and pending events from the successful command model.
- Existing event names, payloads, API response shape, and persistence behavior were preserved.
- Broader immutability work for other budgeting objects remains out of scope.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 17 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 118 tests.

### Increment 7 - Load Effective Budget Through Repository

Status: implemented, awaiting review.

Goal: start moving domain model population out of handlers by moving effective-budget population and EF query shape behind the effective-budget repository boundary.

- Add a repository load method for the temporal command model, preferably on `IEffectiveBudgetRepository`.
- Preferred direction:

```csharp
var effectiveBudget = await effectiveBudgetRepository.GetEffectiveBudget(
    budgetId,
    date,
    ct);
```

- The load boundary should know how to:
  - verify the parent budget exists or otherwise report that it does not.
  - load all budget items needed by the command model.
  - calculate effective planned amounts as of the command date.
  - hydrate `EffectiveBudget` with the item state needed for budget modification commands.
- The handler should coordinate the use case:
  - request the effective budget.
  - validate raw command input such as positive money.
  - call `RecordAdjustment(...)`.
  - inspect `EffectiveBudgetResult` cases.
  - save the successful command model.
- The handler should not contain `HydrateEffectiveBudget(...)`, `EffectivePlannedAmount`, grouped EF queries, or EF projection details for effective planned amounts.
- Preserve the existing 404 behavior for missing budgets and missing budget items.
- Keep data access outside domain objects. Do not add database calls, repository dependencies, or service locators to `EffectiveBudget`, `Budget`, or child domain objects.
- Prefer a closed load result only if needed to preserve handler behavior without exceptions for expected missing data.
- Keep the load result small and named after repository/read concerns, not a generic service response.
- Do not introduce event sourcing, a broad Unit of Work abstraction, or new application services.
- Add focused tests around the repository load path and update handler/API tests only as needed to prove behavior is unchanged.

Rationale:

`EffectiveBudget` is the temporal command model, but calculating and hydrating that model from EF-backed storage is a data access concern. The adjustment handler should not know the persistence anatomy of effective planned amounts any more than it knows how pending adjustments and events are saved. This is the first domain-population cleanup increment; later increments should apply the same boundary to other command models such as `Budget`.

Implementation notes:

- Added `IEffectiveBudgetRepository.GetEffectiveBudget(...)` as the load boundary for the temporal command model.
- Added a closed `EffectiveBudgetLoadResult` with success and missing-budget cases so expected missing-budget outcomes remain result-driven.
- Moved budget existence lookup, budget item loading, effective planned amount grouping, and `EffectiveBudget` hydration from the adjustment handler into `EffectiveBudgetRepository`.
- `GetEffectiveBudget(...)` loads all budget items for the budget/date command model; unknown command item ids remain `EffectiveBudgetResult.ItemNotFound` outcomes from `EffectiveBudget.RecordAdjustment(...)`.
- The adjustment handler now coordinates the use case by loading the command model, validating raw money input, calling `RecordAdjustment(...)`, inspecting domain result cases, and saving the successful command model.
- Preserved existing 404 behavior for missing budgets and missing budget items.
- Kept EF query shape and hydration details out of domain objects.
- Kept transactions, reallocations, event payloads, and the save boundary out of scope.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 8 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 121 tests.

### Increment 8 - Make Budget Item Immutable

Status: implemented, awaiting review.

Goal: remove public mutation from `BudgetItem` while preserving existing archive behavior and event payloads.

- Replace public setters or mutable initialization paths with constructors/factories that produce valid `BudgetItem` instances.
- Keep identity, budget id, name, kind, and archive state as immutable properties.
- Change archive behavior to return a modified `BudgetItem` rather than mutating the original instance where practical.
- If EF compatibility requires a private constructor or backing fields, keep those details private to persistence.
- Ensure existing call sites use the returned archived item where a state change is intended.
- Preserve archive validation behavior and existing budget-item event names/payloads.
- Do not introduce `FundingBudgetItem` or `ConsumptionBudgetItem` subclasses.
- Do not touch transaction or allocation behavior.
- Add focused domain tests proving archive operations do not mutate the original item and existing archive rejection behavior remains stable.

Implementation notes:

- Removed public mutation from `BudgetItem` by replacing public setters and required object initialization with private setters, private EF construction, and factory construction.
- Changed `BudgetItem.Archive(...)` to return an archived `BudgetItem` copy instead of mutating the original instance.
- Added `BudgetItem.ArchivedEvent()` so the archived item produces the existing `BudgetItemArchived` event payload without changing event names or payload shape.
- Updated the archive handler to persist the returned archived item through the EF tracked entry while keeping data access outside the domain object.
- Updated effective-budget archive tests to pass the returned archived item into the command model.
- Added focused domain coverage proving archive does not mutate the original item and the archived event payload remains stable.
- Kept transactions, allocations, reallocations, and broader `Budget` immutability out of scope.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetItem|FullyQualifiedName~BudgetItemsTests"`
- Run broader budgeting tests if call sites change.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetItem|FullyQualifiedName~BudgetItemsTests"` - passed, 20 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 122 tests.

### Increment 9 - Make Budget Immutable

Status: implemented, awaiting review.

Goal: make `Budget` immutable at its public domain surface while preserving budget creation and budget-owned item behavior.

- Remove public setters and mutable initialization requirements from `Budget` where practical.
- Keep `Budget.Create(...)` as the valid public creation path.
- Keep budget-owned item collection exposed as a read-only view.
- Change `CreateBudgetItem(...)` to return a modified `Budget` and the created `BudgetItem`, or another small result shape if needed to preserve clarity.
- Keep duplicate-name validation inside `Budget`.
- Ensure the original `Budget` remains unchanged after creating a budget item.
- Keep EF hydration concerns internal/private and outside vertical slice handlers.
- Do not move transactions, historical adjustments, or reporting state into `Budget`.
- Preserve existing budget and budget-item event names/payloads.
- Add focused domain tests proving successful item creation returns modified budget state and duplicate-name rejection remains stable.

Implementation notes:

- Removed public mutation from `Budget` by replacing public setters and required object initialization with private setters, private EF construction, and factory construction.
- Replaced mutable item loading with `Budget.WithItems(...)`, which returns a reconstructed budget with validated budget-owned items.
- Changed `Budget.CreateBudgetItem(...)` to return the modified `Budget` and created `BudgetItem` instead of mutating the original budget's item collection.
- Kept duplicate-name validation inside `Budget` and preserved the existing duplicate-name message.
- Added an explicit JSON reconstruction constructor so existing API response deserialization remains compatible without reopening public setters.
- Updated the budget-item creation handler to use the returned command model while preserving the current persistence shape and `BudgetItemCreated` event payload.
- Kept transactions, adjustments, reallocations, and broader budget repository loading out of scope.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests"`
- Run broader budgeting tests if repository or handler hydration changes.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests"` - passed, 19 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 122 tests.

### Increment 10 - Load Budget Through Repository

Status: implemented, awaiting review.

Goal: move `Budget` population, including budget-owned items, out of vertical slice handlers and behind a budget repository/data access boundary.

- Add or extend a repository method for loading the command `Budget` shape needed by budget-item commands.
- Preferred direction:

```csharp
var budget = await budgetRepository.GetBudgetWithItems(budgetId, ct);
```

- The load boundary should know how to:
  - verify the budget exists or otherwise report that it does not.
  - load budget-owned items needed for duplicate-name validation and command behavior.
  - hydrate `Budget` using internal/private persistence-facing paths.
- Handlers that create, archive, or otherwise modify budget items should not manually query budget items to assemble a `Budget`.
- Handlers should coordinate the use case:
  - request the `Budget` command model.
  - call domain behavior such as `CreateBudgetItem(...)`.
  - inspect expected result cases or map known domain rejections.
  - persist the successful command model or created records through repository boundaries.
- Preserve the existing API behavior for missing budgets, duplicate budget item names, archived items, and event payloads.
- Keep data access outside `Budget` and `BudgetItem`.
- Do not load transactions, historical adjustments, reporting read models, or projections into `Budget`.
- Do not introduce service locators, event sourcing infrastructure, or a broad Unit of Work abstraction.
- Add focused repository and handler/API tests proving behavior is unchanged while handler-side population is removed.

Rationale:

Once `Budget` owns its item collection, loading that owned collection is part of reconstructing the command model. That reconstruction belongs in data access code, not in each vertical slice handler that happens to need budget-item state.

Implementation notes:

- Added `IBudgetRepository.GetBudgetWithItems(...)` as the load boundary for budget command models that need budget-owned item state.
- Added a closed `BudgetLoadResult` with success and missing-budget cases so expected missing-budget outcomes remain result-driven.
- Implemented `BudgetRepository` using EF-backed persistence to load the budget and budget-owned items, then hydrate `Budget` through its internal reconstruction path.
- Updated the create-budget-item handler to request the hydrated `Budget` command model from the repository instead of manually querying budget items and calling `WithItems(...)`.
- Preserved existing duplicate-name validation inside `Budget`, existing 404 behavior for missing budgets, and existing `BudgetItemCreated` event payload behavior.
- Left archive behavior unchanged because it does not populate a `Budget` command model in the current flow.
- Kept transactions, historical adjustments, reporting read models, and projections out of scope.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests"`
- Run broader budgeting tests if persistence mappings or handler flows change.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests|FullyQualifiedName~BudgetRepositoryTests"` - passed, 25 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 124 tests.

### Increment 11 - Make Budget Adjustment Immutable

Status: implemented, awaiting review.

Goal: ensure `BudgetAdjustment` is an immutable record of a planned budget change.

- Remove public setters or mutable initialization paths from `BudgetAdjustment`.
- Keep factory methods responsible for valid construction.
- Preserve positive-money validation through `PositiveMoneyAmount` for expected command paths.
- Preserve current `BudgetAdjustmentRecorded` event name and payload shape.
- Keep reallocation id support as existing data, but do not refactor reallocation workflows in this increment.
- Keep EF compatibility private/internal.
- Add focused tests proving factory-created adjustments expose immutable state and event payloads remain unchanged.

Implementation notes:

- Removed public mutation from `BudgetAdjustment` by replacing public setters and init setters with private setters, private EF construction, and factory construction.
- Kept `BudgetAdjustment.Create(...)` as the public valid construction path and retained the internal `PositiveMoneyAmount` overload for already-validated command flows.
- Preserved `ReallocationId` as existing adjustment data without refactoring the reallocation workflow.
- Preserved `SignedPlannedAmount()`, `BudgetAdjustmentRecorded` event name, and event payload shape.
- Added focused domain coverage proving the public adjustment surface does not expose setters and the recorded event payload remains stable.
- Kept transactions, allocations, reallocations, persistence shape, and event contracts otherwise unchanged.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~EventContractTests"`
- Run broader event tests if event construction changes.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~EventContractTests"` - passed, 19 tests.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetAdjustmentTests"` - passed, 3 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 125 tests.

### Increment 12 - Make Budget Reallocation Immutable

Status: implemented, awaiting review.

Goal: make `BudgetReallocation` immutable without expanding the scope into transactions, allocation logic, or reporting redesign.

- Remove public setters or mutable initialization paths from `BudgetReallocation`.
- Keep existing factories/constructors as the only valid creation paths, adjusting them as needed to return valid immutable instances.
- Preserve existing reallocation event names, payloads, and persistence shape.
- Keep any related budget adjustments as records created by the command flow; do not move transactions or allocations into `Budget`.
- Keep EF compatibility private/internal.
- Add focused tests around reallocation construction and any existing reallocation API behavior.

Implementation notes:

- Removed public mutation from `BudgetReallocation` by replacing public setters and init setters with private setters, private EF construction, and factory construction.
- Kept `BudgetReallocation.Create(...)` as the public construction path and preserved the existing trimmed-notes behavior.
- Preserved existing reallocation validation, linked adjustment creation, event name, event payload shape, and persistence shape.
- Added focused domain coverage proving the public reallocation surface does not expose setters.
- Kept transactions, allocations, effective-budget behavior, and reallocation workflow redesign out of scope.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"`
- Run broader budgeting tests if reallocation persistence mappings change.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"` - passed, 8 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 126 tests.

### Increment 13 - Finish Effective Budget Immutability Surface

Status: implemented, awaiting review.

Goal: close remaining immutability gaps in `EffectiveBudget` after repository loading and child domain types have been tightened.

- Keep `EffectiveBudget.RecordAdjustment(...)` returning `EffectiveBudgetResult.Success(EffectiveBudget Budget)`.
- Ensure `EffectiveBudget` exposes no mutable collections or mutable child state through its public API.
- Ensure pending adjustments and pending events are immutable snapshots from the caller's perspective.
- Ensure all constructors/factories enforce valid state and keep hydration-only shapes internal.
- Remove or narrow compatibility shims that are no longer needed after handlers use validated value objects and repositories load the command model.
- Keep failure cases non-mutating.
- Preserve existing save boundary behavior and event payloads.
- Add focused public-surface tests that assert implementation details such as item state remain non-public.

Implementation notes:

- Changed `EffectiveBudget` to store item state, pending adjustments, and pending events behind read-only collection wrappers.
- Pending adjustments and pending events remain exposed as `IReadOnlyCollection<T>`, but callers can no longer mutate the returned collection through common collection casts.
- Preserved `EffectiveBudgetResult.Success(EffectiveBudget Budget)` and the successful-command save boundary behavior.
- Preserved failure cases as non-mutating result cases.
- Confirmed `EffectiveBudget` exposes no public constructors or public property setters.
- Kept `EffectiveBudgetItemState` internal and out of the exported command surface.
- Retained the decimal `RecordAdjustment(...)` compatibility overload for this increment because it still preserves expected validation-result behavior and has focused coverage.
- Preserved existing event names, payloads, API behavior, and persistence shape.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"`
- Run `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` if practical.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests"` - passed, 22 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 128 tests.

### Increment 14 - Remove Raw Money Effective Budget Shim

Status: implemented, awaiting review.

Goal: narrow the `EffectiveBudget` command surface so expected raw money validation happens before callers enter the domain command model.

- Remove the decimal `EffectiveBudget.RecordAdjustment(...)` compatibility overload retained in Increment 13.
- Keep `EffectiveBudget.RecordAdjustment(...)` accepting `PositiveMoneyAmount` so callers must supply validated positive money.
- Preserve result-based domain rejections for effective-budget state validation.
- Keep raw decimal validation in the handler and money value object result path.
- Preserve existing event names, payloads, API behavior, persistence behavior, and save boundary behavior.
- Keep transactions, reallocations, and broader money-input refactors out of scope.
- Add focused public-surface coverage proving adjustment commands require validated money.

Implementation notes:

- Removed the public decimal `EffectiveBudget.RecordAdjustment(...)` overload.
- Updated focused domain and repository tests to pass `PositiveMoneyAmount` into the effective-budget command model.
- Added public-surface coverage that asserts `EffectiveBudget.RecordAdjustment(...)` exposes only the validated `PositiveMoneyAmount` command input.
- Kept invalid raw decimal coverage in `PositiveMoneyAmount` tests and the adjustment handler test.
- Preserved existing effective-budget validation result cases, event payloads, persistence behavior, and API behavior.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~MoneyAmountTests"`
- Run `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` if practical.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~MoneyAmountTests"` - passed, 25 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 128 tests.

### Increment 15 - Return Result For Duplicate Budget Item Names

Status: implemented, awaiting review.

Goal: remove an expected domain rejection from the exception path while preserving the existing budget-item API behavior.

- Add a closed result type for `Budget.CreateBudgetItem(...)`.
- Return a duplicate-name result instead of throwing `InvalidOperationException` for the expected duplicate-name rejection.
- Keep successful creation returning the modified immutable `Budget` and created `BudgetItem`.
- Update the create-budget-item handler to inspect the result case and preserve the existing validation response shape.
- Keep budget-item persistence and event payload behavior unchanged.
- Keep transactions, adjustments, reallocations, and broader budget repository changes out of scope.

Implementation notes:

- Added `CreateBudgetItemResult` with `Success(Budget Budget, BudgetItem Item)` and `DuplicateName(string Error)` cases.
- Changed `Budget.CreateBudgetItem(...)` to return result cases for both success and duplicate-name rejection.
- Removed the create-budget-item handler's separate pre-validation call and made the domain command result the single source for duplicate-name handling.
- Preserved the existing duplicate-name API validation field and message.
- Preserved successful `BudgetItemCreated` event behavior and persistence shape.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemsTests|FullyQualifiedName~BudgetRepositoryTests"` - passed, 27 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 128 tests.

### Increment 16 - Verify Budget And Budget Item Public Immutability

Status: implemented, awaiting review.

Goal: close the remaining test coverage gap around Step 15's completion target that budgeting command-domain types involved in effective budget modification no longer expose mutable state as their public shape.

- Add focused public-surface tests for `Budget`.
- Add focused public-surface tests for `BudgetItem`.
- Verify both types expose no public constructors and no public property setters.
- Keep production behavior unchanged.
- Keep transactions, adjustments, reallocations, persistence, and event payloads out of scope.

Implementation notes:

- Added `BudgetDoesNotExposePublicMutationOrConstruction`.
- Added `BudgetItemDoesNotExposePublicMutationOrConstruction`.
- Left production domain and persistence code unchanged because the existing implementation already matched the public immutability target.
- Preserved all existing budget and budget-item behavior.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemTests"` - passed, 21 tests.

### Increment 17 - Verify Adjustment And Reallocation Public Construction

Status: implemented, awaiting review.

Goal: close the same public immutability verification gap for the remaining budgeting command-domain records in Step 15's completion target.

- Add focused public-surface tests for `BudgetAdjustment`.
- Add focused public-surface tests for `BudgetReallocation`.
- Verify both types expose no public constructors and no public property setters.
- Keep production behavior unchanged.
- Keep transactions, allocations, effective-budget behavior, persistence, and event payloads out of scope.

Implementation notes:

- Extended `BudgetAdjustmentDoesNotExposePublicMutationOrConstruction` to assert there are no public constructors.
- Extended `BudgetReallocationDoesNotExposePublicMutationOrConstruction` to assert there are no public constructors.
- Left production domain and persistence code unchanged because the existing implementation already matched the public immutability target.
- Preserved existing adjustment and reallocation behavior.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetAdjustmentTests|FullyQualifiedName~BudgetReallocationTests"` - passed, 9 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 130 tests.

### Increment 18 - Snapshot Budget-Owned Items On Reconstruction

Status: implemented, awaiting review.

Goal: close a remaining public immutability edge where reconstructed `Budget` instances could expose changes made to the source item collection after construction.

- Snapshot budget-owned items during `Budget` reconstruction.
- Expose the reconstructed `Budget.Items` collection as a read-only snapshot.
- Keep `Budget.CreateBudgetItem(...)`, duplicate-name validation, repository loading, persistence shape, and event payloads unchanged.
- Keep transactions, adjustments, reallocations, and broader repository changes out of scope.

Implementation notes:

- Changed the `Budget` reconstruction path to copy item collections into a `ReadOnlyCollection<BudgetItem>`.
- Added focused domain coverage proving mutations to the source collection do not affect `Budget.Items`.
- Added focused domain coverage proving the exposed item collection is read-only from the caller's perspective.
- Preserved existing budget-item creation, duplicate-name validation, and repository hydration behavior.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemTests"` - passed, 22 tests.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetRepositoryTests|FullyQualifiedName~BudgetTests|FullyQualifiedName~BudgetItemTests"` - passed, 28 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 131 tests.

### Increment 19 - Return Result For Linked Reallocation Adjustments

Status: implemented, awaiting review.

Goal: remove an expected reallocation validation rejection from the exception path without redesigning the reallocation workflow.

- Add a closed result type for `BudgetReallocation.CreateLinkedAdjustments(...)`.
- Return a validation result instead of throwing for invalid linked-adjustment input.
- Keep successful linked adjustment creation, persistence shape, and `BudgetReallocationRecorded` event payload behavior unchanged.
- Keep the handler's existing validation ordering and response shape.
- Keep transactions, allocations, effective-budget behavior, and broader reallocation redesign out of scope.

Implementation notes:

- Added `CreateLinkedBudgetAdjustmentsResult` with success and validation-failed cases.
- Changed `BudgetReallocation.CreateLinkedAdjustments(...)` to return the result type and preserve linked `BudgetAdjustment` creation on success.
- Updated the reallocation handler to inspect the result while keeping the existing pre-validation path so API behavior remains stable.
- Added focused domain coverage proving invalid linked adjustments return a validation result instead of throwing.
- Preserved existing reallocation event names, payloads, persistence behavior, and API response behavior.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"` - passed, 9 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - passed, 132 tests.

### Increment 20 - Record Reallocation Through Effective Budget

Status: implemented, awaiting review.

Goal: make `EffectiveBudget` the temporal command model for budget reallocations as well as direct budget adjustments.

- Add `EffectiveBudget.RecordReallocation(...)` as the public command method for reallocating planned budget between budget items at the effective date.
- Keep the command input small and explicit, likely a collection of budget-item id, positive amount, and direction values plus optional notes.
- Introduce or extend closed result cases for expected reallocation rejections:
  - item not found.
  - archived item.
  - invalid reallocation balance.
  - non-consumption budget item.
  - effective planned-position validation failure if the reallocation changes item balances in an invalid way.
- On success, return `EffectiveBudgetResult.Success(EffectiveBudget Budget)` containing a modified effective budget.
- Successful reallocation should add one pending `BudgetReallocation`, the linked pending `BudgetAdjustment` records, and the existing `BudgetReallocationRecorded` domain event to the modified `EffectiveBudget`.
- Update in-memory effective planned amounts for affected items after success so later commands in the same unit of work validate against the modified position.
- Extend the effective-budget save boundary to persist pending reallocations in addition to pending adjustments and pending events.
- Update the reallocation handler so it coordinates the use case:
  - load the effective budget for the command date.
  - validate raw money input before calling the domain command model.
  - call `RecordReallocation(...)`.
  - inspect result cases.
  - save the successful effective budget through the repository boundary.
- The handler should not know the persistence anatomy of a successful reallocation.
- Preserve existing `BudgetReallocationRecorded` and linked adjustment event payload behavior unless a later contract increment explicitly changes it.
- Keep transactions, allocation logic, reporting read-model redesign, and `FinancialTransaction`/`TransactionAllocation` refactors out of scope.
- Do not move data access into `EffectiveBudget`, `BudgetReallocation`, or child domain objects.
- Keep the increment small enough that existing validation response shapes and API behavior remain stable.

Rationale:

Reallocation is also a temporal budget modification at a specific date. It depends on the same command-state concerns as direct adjustments: budget item existence, archive status, budget item kind, point-in-time planned amounts, pending records, and domain events. Moving the behavior behind `EffectiveBudget` keeps vertical slice handlers from coordinating child domain operations and persistence details by hand, while preserving the existing EF-backed command model.

Tests to run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"`
- Run `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` if practical.

Implementation notes:

- Added `EffectiveBudget.RecordReallocation(...)` as the public behavior for recording date-scoped budget reallocations.
- Added a validated-money reallocation command input so raw decimal validation remains outside the effective budget command model.
- Successful reallocations now return a modified `EffectiveBudget` containing one pending `BudgetReallocation`, the linked pending `BudgetAdjustment` records, and the existing `BudgetReallocationRecorded` domain event.
- `EffectiveBudget` updates affected item planned amounts after successful reallocations so later commands in the same unit of work see the modified state.
- Extended the effective-budget save boundary to persist pending reallocations alongside pending adjustments and pending events.
- Updated the reallocation handler to load the effective budget, validate raw money, call the domain command model, inspect result cases, and save through the effective-budget repository.
- Preserved existing reallocation event name, payload shape, linked adjustment persistence, API response shape, and validation response fields.
- Kept transactions, allocation logic, reporting read models, and `FinancialTransaction`/`TransactionAllocation` refactors out of scope.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"` - passed, 34 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false` - failed, 138 passed and 1 failed. The failing test was `BudgetyTzar.Tests.BudgetSnapshotsTests.ProjectionBackedSnapshotReturnsZeroBalancesForBudgetItemsWithoutActivity`, which returned 404 for a projection-backed no-activity snapshot and also failed when rerun by itself.

### Increment 21 - Preserve Projection-Backed No-Activity Snapshots

Status: implemented, awaiting review.

Goal: close the broader-suite failure recorded after Increment 20 without changing the effective-budget command model or transaction boundaries.

- Preserve projection-backed snapshot behavior for active budget items that have no adjustments or transaction activity.
- Keep projection-backed reads from rebuilding from the outbox at read time.
- Keep the fix in reporting projection persistence/calculation code, not in domain command objects.
- Preserve existing event names, payloads, and API response shapes.
- Keep transactions, allocations, and reporting read-model redesign out of scope.

Implementation notes:

- Added a baseline projection snapshot date so projection-backed reports can return active budget items with zero balances before the first dated adjustment or transaction.
- Changed budget-item-created projection handling to recalculate the snapshot timeline from the beginning because active item membership affects historical zero-balance snapshots in the same way as the direct snapshot calculator.
- Preserved projection-backed read behavior: the endpoint still reads durable projection rows and does not rebuild from outbox at read time.
- Left `EffectiveBudget`, reallocation command handling, event contracts, transactions, and allocation behavior unchanged.

Tests run:

- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false /p:UseAppHost=false --filter "FullyQualifiedName~ProjectionBackedSnapshotReturnsZeroBalancesForBudgetItemsWithoutActivity"` - passed, 1 test.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false /p:UseAppHost=false --filter "FullyQualifiedName~ProjectionBackedSnapshot|FullyQualifiedName~ProjectionRebuildFromOutboxSupportsProjectionBackedSnapshots|FullyQualifiedName~ProjectionReadinessApiTests"` - passed, 9 tests.
- `dotnet test tests/BudgetyTzar.Tests/BudgetyTzar.Tests.csproj --no-restore /nr:false /p:UseSharedCompilation=false /p:UseAppHost=false --filter "FullyQualifiedName~EffectiveBudgetTests|FullyQualifiedName~EffectiveBudgetRepositoryTests|FullyQualifiedName~BudgetReallocation|FullyQualifiedName~Reallocation"` - passed, 34 tests.
- `dotnet test --no-restore /nr:false /p:UseSharedCompilation=false /p:UseAppHost=false` - passed, 139 tests.

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
