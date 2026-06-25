# 03 - Budgeting Command Slices

## Goal

Organize budgeting write operations as feature-owned command slices.

## Scope

- Create slices for create budget, budget items, adjustments, and reallocations.
- Keep request, validator, handler, and command endpoint behavior near the feature capability.
- Move existing code first; improve business logic placement only where ownership is obvious.

## Out of scope

- Changing budgeting routes or DTO shapes.
- Replacing EF persistence.
- Adding event-store repositories or aggregate base classes.
- Rewriting projection behavior.

## Files likely affected

- `src/BudgetyTzar.Api/Features/Budgeting/**`
- `src/BudgetyTzar.Api/Application/Budgeting/**`
- `src/BudgetyTzar.Api/Features/DependencyInjection.cs`

## Invariants to preserve

- Budgeting routes and status codes.
- Budgeting event payloads and outbox behavior.
- Existing validation messages.
- Archived budget item correction rules.
- Database writes and transaction boundaries.

## Implementation checklist

- Move create budget request/validator/handler into a create-budget slice.
- Move create/archive budget item request/validator/handler into a budget-item slice.
- Move adjustment request/DTO/validator/handler into an adjustments slice.
- Move reallocation request/DTO/validator/handler into a reallocations slice.
- Keep public namespaces stable where tests or API surface depend on them.
- Keep shared archived-item validation support only while multiple features still need it.

## Tests to run

- Focused budgeting, contract, and domain tests.
- `dotnet test --no-restore`

## Completion notes

- This step has been implemented for current budgeting commands. Remaining work is later cleanup of shared budgeting support once ownership is clearer.
