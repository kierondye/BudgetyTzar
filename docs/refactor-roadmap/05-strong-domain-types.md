# 05 - Strong Domain Types

## Goal

Introduce explicit domain types where they improve clarity, correctness, or business language.

## Scope

- Introduce types incrementally inside touched slices.
- Prefer boundary mapping from primitives to domain types.
- Build on existing `MoneyAmount` and `DateRange` concepts where practical.

## Out of scope

- Converting all EF entities, JSON contracts, or event schemas at once.
- Introducing types purely for architectural purity.
- Large-scale serialization or migration changes.

## Files likely affected

- `src/BudgetyTzar.Api/Domain/Common/**`
- `src/BudgetyTzar.Api/Domain/Budgeting/**`
- `src/BudgetyTzar.Api/Domain/Transactions/**`
- Touched feature slice files.

## Invariants to preserve

- Public API contracts remain primitive-friendly.
- Event payload records and schemas remain stable unless separately approved.
- EF mappings and database schema remain stable.
- Validation errors remain compatible.

## Implementation checklist

- Start with high-value concepts: `BudgetId`, `BudgetItemId`, `TransactionId`, `Money`, `Currency`, `DateRange`.
- Add `CategoryId` and `Month` only when active code has those concepts.
- Map HTTP primitives into domain types at command boundaries.
- Keep value-object validation close to the type.
- Avoid anonymous dictionaries or loosely typed object graphs inside business logic when a named concept is clearer.

## Tests to run

- Domain tests for each new value object.
- Focused feature tests for touched slices.
- Contract tests if boundary mapping changes.
- `dotnet test --no-restore`

## Completion notes

- Implemented a first narrow increment by adding a `Currency` value object and using it inside `Budget.Create`.
- Decision: keep `Budget.Currency`, API DTOs, EF mappings, and event payloads string-based for now so HTTP contracts, event schemas, and database schema remain unchanged.
- Decision: leave API-facing validation in `CreateBudgetValidator`/`MoneyRules` to preserve validation behavior and messages; the domain value object protects direct domain creation paths.
- TODO: introduce identifier value objects such as `BudgetId`, `BudgetItemId`, and `TransactionId` only when an active slice can map them at the boundary without broad EF or contract churn.
- TODO: continue moving decimal amount handling toward `MoneyAmount` during future touched command/domain slices.
