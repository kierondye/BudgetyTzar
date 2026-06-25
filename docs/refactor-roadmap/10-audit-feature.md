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
