# 06 - Shared Cleanup

## Goal

Keep shared code minimal and move feature-specific concepts back to their owning feature or domain.

## Scope

- Review existing shared/common helpers.
- Keep only genuine cross-cutting plumbing in shared locations.
- Move budgeting and transaction-specific helpers into feature/domain homes.

## Out of scope

- Removing useful concrete domain services.
- Creating new generic helper layers.
- Changing behavior while relocating helpers.

## Files likely affected

- `src/BudgetyTzar.Api/Features/Shared/**`
- `src/BudgetyTzar.Api/Application/Common/**`
- `src/BudgetyTzar.Api/Application/Budgeting/**`
- `src/BudgetyTzar.Api/Features/**`

## Invariants to preserve

- Validation messages.
- HTTP result mapping behavior.
- Shared DTO response shapes.
- Any helper behavior used by tests.

## Implementation checklist

- Keep generic command result and HTTP mapping only if genuinely cross-cutting.
- Move budgeting-specific rules into budgeting domain/feature files.
- Move transaction-specific helpers into transaction feature/domain files.
- Avoid placing business concepts in `Shared` because multiple classes use them.
- Delete empty marker files and dead namespaces.

## Tests to run

- Focused tests for any affected feature.
- `dotnet test --no-restore`

## Completion notes

- Partially started: transaction allocation formatting now lives with transaction allocations. More shared cleanup remains.
- Removed the transaction allocation status helper from `Features/Shared`; transaction list filtering now uses
  `FinancialTransaction.GetAllocationStatus`, keeping that transaction-specific rule in the transaction domain model.
- Moved `TransactionAllocationItem` beside the transaction allocation capability and `BudgetReallocationAdjustmentItem`
  beside the reallocation capability. Removed `Features/Shared/SharedDtos.cs`.
- Decision: keep both moved record types in the existing `BudgetyTzar.Api` namespace for now so public type names, JSON
  shapes, request bodies, and tests remain unchanged. This creates a small namespace/file-location mismatch that can be
  revisited if public API namespace cleanup is planned.
- Decision: leave `CommandResult`, HTTP result mapping, and budget lookup in place
  until a future focused cleanup can move or justify each one without changing API contracts.
- Validation: manual follow-up validation passed with `dotnet build BudgetyTzar.sln` and `dotnet test`.
- Moved `MoneyRules` from `Features/Shared` to `Features/Validation` because the amount and currency rules are
  feature-boundary validation plumbing used across budgeting and transaction command validators. Method names,
  validation behavior, and validation messages remain unchanged.
- Decision: leave `BudgetLookup` in `Features/Shared` for now because it is used by budgeting, transaction query, and
  reporting endpoints; ownership is not clear enough for a narrow move without duplicating logic or introducing a new
  abstraction.
- Validation: `dotnet build BudgetyTzar.sln` passed with 0 warnings and 0 errors; `dotnet test` passed with 77 tests.
- TODO: review `BudgetLookup` in a later step 06 increment.
