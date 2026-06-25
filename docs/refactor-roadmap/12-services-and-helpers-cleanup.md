# 12 - Services and Helpers Cleanup

## Goal

Remove or relocate procedural services and helpers where ownership is clear.

## Scope

- Review classes named `Service`, `Helper`, and broad utility wrappers.
- Keep concrete domain services that express real business concepts.
- Move feature-specific helpers into owning feature folders.

## Out of scope

- Removing services that are still cross-feature and cohesive.
- Introducing replacement abstractions.
- Cleanup that changes behavior without focused tests.

## Files likely affected

- `src/BudgetyTzar.Api/Application/**`
- `src/BudgetyTzar.Api/Features/**`
- `src/BudgetyTzar.Api/Domain/**`
- `src/BudgetyTzar.Api/Infrastructure/**`

## Invariants to preserve

- Behavior and validation messages.
- Public API shape.
- Event payloads and outbox behavior.
- Projection behavior.

## Implementation checklist

- Identify helper/service classes after slice moves.
- Inline procedural wrappers only when call sites remain easy to understand.
- Move feature-owned helpers into feature folders.
- Keep domain services when they hold business rules shared by multiple slices.
- Leave TODOs where ownership is unclear.

## Tests to run

- Focused tests for any touched helper/service owner.
- `dotnet test --no-restore`

## Completion notes

- Partially started through transaction allocation helper relocation. Broader review remains.
