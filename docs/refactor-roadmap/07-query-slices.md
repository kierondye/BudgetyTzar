# 07 - Query Slices

## Goal

Separate read/query behavior by capability and keep it beside the read model or feature it serves.

## Scope

- Move read-only endpoints into query slice files.
- Keep simple EF-backed queries concrete where that is currently the simplest option.
- Ensure queries do not start depending on aggregates as the event-sourced direction matures.

## Out of scope

- Introducing mediator/query abstractions.
- Changing response shapes or filters.
- Replacing query persistence with projections unless that is a separate reporting/read-model step.

## Files likely affected

- `src/BudgetyTzar.Api/Features/Budgeting/**`
- `src/BudgetyTzar.Api/Features/Transactions/**`
- `src/BudgetyTzar.Api/Features/Reporting/**`
- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`

## Invariants to preserve

- Routes, filters, sorting, and validation messages.
- Not-found behavior.
- Response DTOs and JSON shape.
- Query performance characteristics unless intentionally changed later.

## Implementation checklist

- Move transaction list/detail/allocation queries into transaction query slices.
- Move budget list/detail and budget-item list queries into budgeting query slices.
- Move reporting queries beside reporting read models.
- Keep command endpoint composition stable.

## Tests to run

- Focused query tests for touched feature.
- Contract/API surface tests.
- `dotnet test --no-restore`

## Completion notes

- Transaction query slices have been implemented. Budgeting and reporting query slices remain.
- Implemented a narrow budgeting query increment by moving the top-level budget list and budget detail endpoints into
  `Features/Budgeting/ListBudgets` and `Features/Budgeting/GetBudget`.
- Decision: keep the query slices as private mapping methods on the existing `Endpoints` partial in the
  `BudgetyTzar.Api.Features` namespace, matching the completed transaction query-slice pattern and preserving endpoint
  composition, route metadata, response bodies, and tests.
- Decision: keep the EF queries concrete inside the query slice files; no mediator, repository, or read-model
  abstraction was introduced.
- Deferred follow-on: budget-item list queries and reporting queries remain in their current endpoint files for future
  focused increments of this roadmap step.
- Deferred follow-on: `BudgetLookup` remains in `Features/Shared` until query/reporting ownership is clearer.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` first passed compilation but had one Kafka projection consumer timeout; rerunning that test class passed,
  and a second full `dotnet test` passed with 77 tests.
- Implemented a second narrow budgeting query increment by moving the budget-item list endpoint into
  `Features/Budgeting/ListBudgetItems`.
- Decision: keep the budget-item query as a private mapping method on the existing `Endpoints` partial, matching the
  transaction and budget query slice pattern while preserving route metadata and response shape.
- Decision: keep the concrete EF no-tracking query and existing `BudgetExists` check unchanged; no repository, mediator,
  read-model abstraction, or shared lookup ownership change was introduced.
- Deferred follow-on: budget adjustment list and budget reallocation list queries remain as the final budgeting query
  slices before reporting query work starts.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped. `dotnet test` first passed
  compilation but had the same Kafka projection consumer timeout; rerunning that test class passed, and a second full
  `dotnet test` passed with 77 tests.
- Implemented a third narrow budgeting query increment by moving the budget adjustment list endpoint into
  `Features/Budgeting/ListBudgetAdjustments`.
- Decision: keep the adjustment list as a private mapping method on the existing `Endpoints` partial, preserving the
  route, Swagger metadata, not-found behavior, DTO shape, ordering, and legacy `Guid.Empty` budget-id normalization.
- Decision: keep the concrete EF no-tracking query and budget-item ownership check unchanged; no repository, mediator,
  read-model abstraction, or `BudgetLookup` ownership change was introduced.
- Deferred follow-on: budget reallocation list remains as the last budgeting query slice before reporting query work.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped. `dotnet test` passed with
  77 tests.
- Implemented the final budgeting query increment by moving the budget reallocation list endpoint into
  `Features/Budgeting/ListBudgetReallocations`.
- Decision: keep the reallocation list as a private mapping method on the existing `Endpoints` partial, preserving the
  route, Swagger metadata, not-found behavior, budget filtering, date/created ordering, grouped adjustment loading, DTO
  shape, and empty adjustment fallback.
- Decision: keep the concrete EF no-tracking queries and existing `BudgetExists` check unchanged; no repository,
  mediator, read-model abstraction, generic query framework, or `BudgetLookup` ownership change was introduced.
- Budgeting query slicing is complete for the current endpoint set. Reporting queries remain for future Step 07
  increments.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped. `dotnet test` passed with
  77 tests.
