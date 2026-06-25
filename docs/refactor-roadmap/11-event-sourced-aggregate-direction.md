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

- Not started as a dedicated step. Some existing aggregate methods already emit domain events.
