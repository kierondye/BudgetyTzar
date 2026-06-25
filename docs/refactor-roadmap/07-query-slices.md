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
