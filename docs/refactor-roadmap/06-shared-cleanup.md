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
