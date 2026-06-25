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
- Continued with a processing lifecycle boundary increment by extracting `ProjectionProcessingStore` from
  `ReportingProjectionConsumerService`.
- Decision: keep `ProjectionProcessingStore` in `Infrastructure/Events` because claiming projection events, enforcing
  processing leases, observing completed duplicates, marking completion, and marking failed processing rows are
  checkpoint/persistence mechanics owned by the projection runner infrastructure.
- Decision: leave `ReportingProjectionConsumerService` as the orchestration boundary for Kafka consumption, outbox
  rebuild flow, retry/dead-letter behavior, and readiness notifications. It now coordinates the processing store and the
  feature-owned `ReportingProjectionDispatcher` without directly mutating `ProcessedProjectionEvent` rows.
- Decision: keep `ProcessedProjectionEvent` and `ProjectionProcessingStatus` in their existing namespace and keep
  `BudgetDbContext` mappings unchanged, preserving EF model identity, table names, migrations, and readiness query
  behavior.
- Grouping rationale: claim, duplicate-completed check, completion marking, and failure marking are one tightly coupled
  processing-row lifecycle. Moving them together avoids a half-extracted lease/checkpoint boundary while still leaving
  unrelated dead-letter failure persistence for a separate review.
- Preserved behavior: projection idempotency, active-claim skipping, stale claim reclamation, failed-row marking,
  completed-row fields, outbox rebuild behavior, projection readiness notifications, API responses, event contracts,
  Kafka topics, dead-letter message semantics, EF mappings, and database schema remain unchanged.
- Deferred follow-on: `ProjectionEventFailure` persistence and dead-letter metadata parsing still live inside
  `ReportingProjectionConsumerService`; this is the next likely infrastructure-only extraction if Step 09 continues.
- Remaining Step 09 work: consider extracting projection failure/dead-letter persistence as a concrete infrastructure
  helper, then reassess whether the consumer boundary is small enough without introducing generic frameworks or changing
  runtime semantics.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
- Continued with a projection failure persistence boundary increment by extracting `ProjectionFailureStore` from
  `ReportingProjectionConsumerService`.
- Decision: keep `ProjectionFailureStore` in `Infrastructure/Events` because `ProjectionEventFailure` upserts,
  raw-event metadata parsing, failure-row retry/status updates, and error truncation are operational persistence
  mechanics owned by projection-runner infrastructure.
- Decision: leave dead-letter payload construction and Kafka publishing in `ReportingProjectionConsumerService`; those
  are transport/message semantics rather than persistence concerns.
- Decision: keep `ProjectionEventFailure`, `ProjectionFailureCategory`, and `ProjectionFailureStatus` in their existing
  namespace and keep EF mappings unchanged, preserving model identity, table names, migrations, indexes, and stored
  failure values.
- Grouping rationale: failure upsert behavior, metadata extraction, event-id key fallback, and truncation are tightly
  coupled around durable projection failure records. Moving them together completes the prior deferred infrastructure
  persistence extraction without touching retry policy or dead-letter publication behavior.
- Preserved behavior: validation/projection/dead-letter-publish failure categories, pending/dead-lettered/retryable
  statuses, retry counts, first/last failure timestamps, raw event JSON, dead-letter key fallback, dead-letter payload
  shape, Kafka topics, API responses, event contracts, EF mappings, database schema, projection idempotency, rebuild
  behavior, and readiness notifications remain unchanged.
- Deferred follow-on: `ReportingProjectionConsumerService` still owns retry delay calculation and outbox replay loading.
  Those remain infrastructure orchestration details; extract only if the consumer boundary needs further tightening
  without changing runtime semantics.
- Remaining Step 09 work: reassess whether the projection runner boundary is now sufficiently clear, with feature-owned
  dispatch separated from infrastructure-owned transport, processing lifecycle, and failure persistence.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped after matching the known baseline
  caveat. The first `dotnet test` compile caught a missing EF Core using in the consumer after extraction; after fixing
  it, `dotnet test` passed with 77 tests.
- Continued with an outbox rebuild persistence boundary increment by extracting `ProjectionRebuildStore` from
  `ReportingProjectionConsumerService`.
- Decision: keep `ProjectionRebuildStore` in `Infrastructure/Events` because clearing projection/read-model state and
  loading ordered, schema-validated outbox envelopes are rebuild persistence mechanics owned by projection-runner
  infrastructure.
- Decision: leave rebuild orchestration in `ReportingProjectionConsumerService`; it still decides when to perform full
  or budget-scoped rebuilds and replays each validated envelope through the same processing store, feature dispatcher,
  and readiness notification path.
- Grouping rationale: full reset, budget-scoped reset, and outbox replay loading are one coherent rebuild concern. Moving
  them together removes the last direct EF projection-state manipulation from the consumer without changing replay
  ordering, filtering, or validation.
- Preserved behavior: the same projection/read-model tables are cleared, the same budget-scoped filters are used, the
  same `BudgetId != null` outbox filter is used, outbox envelopes are still ordered by `CreatedAt`, schema validation
  still occurs before replay, and projection idempotency, rebuild behavior, readiness notifications, API responses,
  event contracts, Kafka topics, EF mappings, and database schema remain unchanged.
- Deferred follow-on: `ReportingProjectionConsumerService` still owns retry delay calculation, Kafka consumer/producer
  construction, dead-letter payload construction, and high-level projection orchestration. These are infrastructure
  runner concerns; avoid extracting them unless a future review finds the consumer boundary still too broad.
- Remaining Step 09 work: review for closure. Feature projection dispatch is separated from infrastructure-owned
  transport, processing lifecycle, failure persistence, and rebuild persistence without introducing generic frameworks
  or changing runtime semantics.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
- Closed Step 09 with a boundary review increment.
- Decision: treat Step 09 as complete for the current reporting projection runner. Feature-owned projection dispatch and
  typed payload deserialization now live beside the snapshot projection behavior, while infrastructure owns Kafka
  transport, envelope validation, retry/dead-letter flow, processing leases/checkpoints, failure persistence, rebuild
  persistence, and readiness notifications.
- Decision: intentionally leave retry delay calculation, Kafka consumer/producer construction, dead-letter payload
  construction, and high-level projection orchestration in `ReportingProjectionConsumerService`. These are cohesive
  projection-runner concerns, and extracting them now would add indirection without improving feature/infrastructure
  ownership.
- Decision: keep processing/failure/readiness entity namespaces and EF mappings unchanged. Moving `ProcessedProjectionEvent`
  or `ProjectionEventFailure` namespaces would create migration/model churn without a Step 09 behavior benefit.
- Grouping rationale: this increment is documentation-only because the remaining code in the consumer is infrastructure
  orchestration rather than feature projection behavior. Closing the step records the stopping point and avoids
  speculative helper extraction.
- Preserved behavior: no runtime code changed. API responses, event contracts, event schemas, Kafka topics, dead-letter
  message semantics, database schema, EF mappings, projection idempotency, rebuild behavior, failure semantics, and
  readiness notifications remain unchanged.
- Deferred follow-on: audit projection runner boundaries remain Step 10 work. Any future split of Kafka runner
  construction or retry timing should be driven by a concrete operational need, not by Step 09.
- Remaining Step 09 work: none known for the current reporting projection runner.
- Validation: `dotnet build BudgetyTzar.sln` again hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
