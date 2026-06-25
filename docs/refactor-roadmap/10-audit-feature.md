# 10 - Audit Feature

## Goal

Promote audit into its own feature focused on queryable audit history.

## Scope

- Move audit projection behavior beside audit read-model/query capability.
- Keep audit entries append-only.
- Keep transport parsing and consumer failure handling in infrastructure.

## Out of scope

- Treating audit as the authoritative event record.
- Replacing the event store/outbox.
- Changing audit response shape or failure schema.

## Files likely affected

- `src/BudgetyTzar.Api/Application/Reporting/AuditEventProjectionService.cs`
- `src/BudgetyTzar.Api/Infrastructure/Events/AuditEventConsumerService.cs`
- `src/BudgetyTzar.Api/Features/Reporting/ReportEndpoints.cs`
- Future `src/BudgetyTzar.Api/Features/Audit/**`
- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`

## Invariants to preserve

- Audit events remain append-only.
- Event store/outbox remains authoritative.
- Existing audit API behavior.
- Audit dead-letter/failure handling.
- Audit event descriptions/details compatibility.

## Implementation checklist

- Create an audit feature folder, likely `Features/Audit/AuditEntry`.
- Move audit read/query endpoint code into the audit feature.
- Move audit projection code beside the audit entry read model where practical.
- Keep raw JSON/envelope parsing outside feature code where feasible.
- Preserve failure persistence in infrastructure.

## Tests to run

- Audit projection tests.
- Audit and outbox tests.
- Event projection tests touching audit.
- `dotnet test --no-restore`

## Completion notes

- Started with a narrow audit query ownership increment by moving the audit-events endpoint from
  `Features/Reporting/ListAuditEvents` to `Features/Audit/ListAuditEvents`.
- Also moved `AuditEventDto` beside the audit-events query under `Features/Audit/ListAuditEvents`.
- Decision: keep the endpoint on the existing `Endpoints` partial in the `BudgetyTzar.Api.Features` namespace so route
  composition, Swagger metadata, tests, and API behavior remain unchanged.
- Decision: keep `AuditEventDto` in the existing `BudgetyTzar.Api.Application.Reporting` namespace so tests and public
  type references remain stable. This is a file/folder ownership move only.
- Decision: keep the concrete EF no-tracking query unchanged and continue using the existing `BudgetExists` helper; no
  repository, mediator, read-model abstraction, generic query framework, route change, response-shape change, or schema
  change was introduced.
- Preserved behavior: `/api/budgets/{budgetId}/audit-events`, `from`/`to` filters, not-found behavior, ordering,
  `AuditEventDto` JSON shape, audit projection behavior, audit failure/dead-letter handling, event contracts, event
  schemas, EF mappings, migrations, and database schema remain unchanged.
- Deferred follow-on: move audit projection behavior beside audit read/query ownership in a later Step 10 increment,
  while keeping raw JSON/envelope parsing and consumer failure handling in infrastructure.
- Deferred follow-on: audit failure persistence still lives inside `AuditEventConsumerService`; extract it only as a
  separate infrastructure boundary increment if Step 10 continues into consumer failure handling.
- Remaining Step 10 work: audit projection ownership and audit consumer/failure boundary review remain.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
- Continued with an audit projection ownership increment by moving `AuditEventProjectionService` from
  `Application/Reporting` to `Features/Audit/Projection`.
- Decision: treat `AuditEventProjectionService` as audit feature code because it owns audit-specific canonical event
  type mapping, description/detail generation, idempotent append-only audit row creation, and the audit projection
  result returned after applying an envelope.
- Decision: keep `AuditEventProjectionService` and `AuditProjectionResult` in the existing
  `BudgetyTzar.Api.Application.Reporting` namespace so DI registration, infrastructure consumer references, tests, and
  public type references remain stable. This is a file/folder ownership move only.
- Decision: leave `EventSchemaValidator`, `AuditEventConsumerService`, retry timing, dead-letter publishing, failure
  marking, raw event metadata parsing, Kafka consumer/producer construction, and offset commit behavior in
  infrastructure.
- Grouping rationale: the service and its result record are a tightly coupled audit projection unit. Moving them
  together advances audit feature ownership without mixing in the separate infrastructure failure-persistence boundary.
- Preserved behavior: audit events remain append-only and idempotent, audit descriptions/details remain compatible,
  `/api/budgets/{budgetId}/audit-events` remains unchanged, event contracts, event schemas, envelope validation,
  dead-letter behavior, failure persistence, EF mappings, migrations, and database schema remain unchanged.
- Deferred follow-on: audit failure persistence still lives inside `AuditEventConsumerService`; extract it only as a
  separate infrastructure boundary increment if Step 10 continues into consumer failure handling.
- Remaining Step 10 work: audit consumer/failure boundary review remains. Audit query ownership and audit projection
  behavior are now colocated under `Features/Audit`.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
- Continued with an audit failure persistence boundary increment by extracting `AuditFailureStore` from
  `AuditEventConsumerService`.
- Decision: keep `AuditFailureStore` in `Infrastructure/Events` because `AuditEventFailure` upserts, raw-event metadata
  parsing, dead-letter key fallback, and error truncation are operational persistence mechanics owned by the audit
  consumer infrastructure.
- Decision: leave Kafka consumer/producer construction, topic subscription, retry timing, dead-letter payload
  construction/publishing, schema validation entry point, and offset commits in `AuditEventConsumerService`.
- Grouping rationale: failure-row lookup, insert/update behavior, metadata extraction, event-id fallback, retry/status
  updates, and error truncation are one tightly coupled persistence concern. Moving them together follows the existing
  `ProjectionFailureStore` pattern without mixing in audit feature behavior or changing consumer semantics.
- Preserved behavior: validation/audit-projection/dead-letter-publish failure categories, pending/dead-lettered/retryable
  statuses, retry counts, first/last failure timestamps, raw event JSON, dead-letter key fallback, dead-letter payload
  shape, Kafka topics, offset commit behavior, audit API responses, event contracts, event schemas, EF mappings,
  migrations, and database schema remain unchanged.
- Deferred follow-on: `AuditEventFailure` remains in the existing application reporting namespace to avoid EF model and
  migration churn. Revisit only if a future audit ownership cleanup can preserve model identity and table mappings.
- Remaining Step 10 work: review for closure. Audit query ownership, audit projection behavior, and audit failure
  persistence boundaries are now separated without changing runtime behavior.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped after matching the known baseline
  caveat. `dotnet test` passed with 77 tests.
