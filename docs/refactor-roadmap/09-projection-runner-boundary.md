# 09 - Projection Runner Boundary

## Goal

Separate projection infrastructure mechanics from feature projection behavior.

## Scope

- Keep Kafka, retries, dead-lettering, checkpointing/leases, envelope validation, and dispatch in infrastructure.
- Move feature projection handlers beside the read models they maintain.
- Ensure feature projections receive typed payloads or typed domain events, not raw Kafka messages.

## Out of scope

- Creating a generic event dispatch framework.
- Rewriting the event store/outbox.
- Changing topic names, envelope shape, or schema validation behavior.

## Files likely affected

- `src/BudgetyTzar.Api/Infrastructure/Events/**`
- `src/BudgetyTzar.Api/Application/Reporting/**`
- `src/BudgetyTzar.Api/Features/Reporting/**`
- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`

## Invariants to preserve

- Projection idempotency.
- Lease/retry/dead-letter behavior.
- Outbox rebuild behavior.
- Projection readiness notifications.
- Failure persistence.
- Event schema validation.

## Implementation checklist

- Identify infrastructure-only methods in projection consumers.
- Keep envelope deserialization and validation in infrastructure.
- Dispatch typed payloads to feature-owned projection handlers.
- Keep projections focused on read-model updates.
- Do not let projections contain business decisions.

## Tests to run

- Projection processing tests.
- Kafka projection consumer tests.
- Projection readiness API tests.
- Contract/event schema tests.
- `dotnet test --no-restore`

## Completion notes

- Not started.
