# 11 - Event-Sourced Aggregate Direction

## Goal

Move gradually toward aggregates making decisions and emitting domain events as first-class outputs.

## Scope

- Improve touched aggregates so business decisions live in domain methods.
- Keep commands as the write path.
- Leave extension points for event-store-backed loading/saving where useful.

## Out of scope

- New event-store-backed repository abstractions unless required by a specific slice.
- Aggregate base classes.
- Generic event dispatch frameworks.
- Rehydrating all aggregates from streams in one pass.
- Replacing current EF persistence in a broad refactor.

## Files likely affected

- `src/BudgetyTzar.Api/Domain/Budgeting/**`
- `src/BudgetyTzar.Api/Domain/Transactions/**`
- Touched command slices under `src/BudgetyTzar.Api/Features/**`
- `src/BudgetyTzar.Api/Infrastructure/Events/**`

## Invariants to preserve

- Current persistence behavior until a dedicated slice changes it.
- Existing domain event names and payloads.
- Command API behavior.
- Read models remain projection-owned.
- Queries do not use aggregates as the source of reporting truth.

## Implementation checklist

- Move obvious business rules from handlers into aggregate/value-object methods.
- Prefer aggregate methods that return or expose domain events where already natural.
- Keep handlers focused on load, invoke domain behavior, persist, return result.
- Add TODOs for future event-store loading/saving instead of speculative infrastructure.
- Avoid creating generic event-sourcing machinery.

## Tests to run

- Domain tests for moved rules.
- Focused command tests for touched slices.
- Event contract tests if event creation changes internally.
- `dotnet test --no-restore`

## Completion notes

- Previously not started as a dedicated step. Some existing aggregate methods already emitted domain events.
- Started with a narrow reallocation aggregate increment by moving the reallocation adjustment count and zero-sum
  balancing rules from `RecordReallocationHandler` into `BudgetReallocation`.
- Decision: add a concrete `BudgetReallocationAdjustment` domain input record and keep the handler responsible for
  mapping API request items into that domain shape. This avoids introducing mediator, repository, aggregate base, or
  event-sourcing framework abstractions.
- Decision: let `BudgetReallocation` validate the grouped adjustment invariant and create linked `BudgetAdjustment`
  rows for persistence, while the handler continues to own budget existence checks, budget-item lookup, archived-item
  eligibility, EF persistence, outbox writing, and HTTP result mapping.
- Preserved behavior: `/api/budgets/{budgetId}/reallocations`, request/response JSON, validation messages, event names,
  event payload records, JSON schemas, outbox behavior, EF mappings, migrations, database schema, projections, and
  snapshot results remain unchanged.
- Validation before implementation: baseline `dotnet test` compiled and ran 77 tests; the known flaky Kafka projection
  consumer timeout occurred in `ReportingProjectionConsumerProjectsEventsConsumedFromKafka` with 76 tests passing.
- Validation during implementation: focused `dotnet test --filter "FullyQualifiedName~BudgetReallocation"` passed with
  4 tests.
- Final validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with
  80 tests.
- Deferred follow-on: net planned spending validation in the budget adjustment handler is another candidate for a future
  Step 11 increment, but it was left unchanged to keep this increment focused on reallocations.
- Deferred follow-on: broader event-store loading/saving, identifier value objects, and cleanup of existing feature
  DTO/domain coupling remain out of scope for this increment.
- Remaining Step 11 work: continue moving obvious command-handler business rules into the owning aggregate or value
  object one slice at a time while preserving current persistence and contracts.
