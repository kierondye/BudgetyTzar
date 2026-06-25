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

- Started with a narrow reporting projection dispatch boundary increment by moving the reporting event-type switch and
  typed payload deserialization out of `Infrastructure/Events/ReportingProjectionConsumerService` and into
  `Features/Reporting/Snapshots/ReportingProjectionDispatcher`.
- Decision: keep Kafka consumption, envelope validation, retry/dead-letter handling, projection processing leases,
  failure persistence, outbox rebuild orchestration, and projection readiness notifications in infrastructure. The
  infrastructure consumer now calls a feature-owned dispatcher after the envelope has been validated and claimed.
- Decision: keep `ReportingProjectionDispatcher` in the existing `BudgetyTzar.Api.Application.Reporting` namespace so
  feature DI, tests, and type references remain stable. No mediator, generic dispatch framework, repository, or
  event-sourcing framework was introduced.
- Decision: keep `ReportingProjectionService` unchanged as the concrete snapshot projection handler that receives typed
  budgeting and transaction payload records and maintains the snapshot read models.
- Preserved behavior: event type names, payload records, JSON schemas, envelope shape, Kafka topics, dead-letter payload
  shape, projection idempotency, readiness notifications, failure marking, EF mappings, database schema, rebuild
  behavior, and reporting API responses remain unchanged.
- Deferred follow-on: projection processing state (`ProcessedProjectionEvent`) and projection failure state
  (`ProjectionEventFailure`) still live in `Application/Reporting`; a later Step 09 increment should decide whether
  those processing mechanics move closer to infrastructure without changing table mappings or retry semantics.
- Deferred follow-on: audit projection/query ownership remains Step 10 work. Projection readiness/status and SSE
  endpoint ownership remains deferred unless a later Step 09 increment naturally touches it.
- Remaining Step 09 work: continue identifying infrastructure-only methods inside the projection consumer and extract
  processing/checkpoint/failure mechanics only where the boundary can be tightened without changing behavior.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped after matching the known baseline
  caveat. The first `dotnet test` compiled but hit the existing transient Kafka/audit SQLite active-statement failure;
  the focused failing Kafka projection test passed on rerun, and a final full `dotnet test` passed with 77 tests.
