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

- Not started.
