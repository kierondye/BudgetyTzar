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
- Continued with a narrow projection notification helper ownership increment by moving `ProjectionNotificationService`
  and `ProjectionReadyNotification` from `Application/Reporting` to
  `Features/Reporting/GetProjectionEvents/ProjectionNotificationService.cs`.
- Decision: treat the notification service as reporting feature plumbing for the projection-events SSE endpoint. The
  infrastructure projection runner still publishes notifications after projection completion, but it no longer owns the
  service registration.
- Decision: keep the moved types in the existing `BudgetyTzar.Api.Application.Reporting` namespace so endpoint,
  infrastructure consumer, tests, and serialized notification references remain unchanged. This is file/DI ownership
  cleanup only.
- Decision: register `ProjectionNotificationService` from `AddReportingFeature` as the same singleton lifetime and
  remove the duplicate ownership from infrastructure registration. No mediator, event framework, transport abstraction,
  or API helper layer was introduced.
- Preserved behavior: projection-events route, SSE event name, notification JSON, projection readiness/status behavior,
  Kafka topics, event contracts, event schemas, outbox behavior, EF mappings, migrations, database schema, and tests
  remain unchanged.
- Deferred follow-on: `BudgetItemEligibilityService` remains in `Application/Budgeting` because it supports both
  budgeting and transaction allocation correction rules. `CommandResult`, `BudgetLookup`, processing/failure entities,
  and audit failure entity ownership remain deferred until a focused cleanup can move or justify them without API,
  event, or EF model churn.
- Remaining Step 12 work: continue reviewing services and helpers one at a time; no broader cleanup was attempted in
  this increment.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  `dotnet test` passed with 94 tests.
- Continued with a command-result ownership cleanup by moving `CommandResultStatus`, `CommandResult<T>`, and
  `CommandResult` from `Application/Common` to `Features/Shared/CommandResult.cs`.
- Decision: treat command results as feature command orchestration plumbing because they are only used by feature
  command handlers and the feature HTTP result mapper. They are not domain concepts, infrastructure concerns, or
  persistence models.
- Decision: colocate the result types with `CommandResultHttpExtensions` under `Features/Shared` and remove the old
  `BudgetyTzar.Api.Application.Common` imports from command slices. This keeps the shared surface intentionally small:
  one concrete command result shape plus its HTTP mapping.
- Grouping rationale: the result records, status enum, and HTTP mapper are one tightly coupled command-response concern.
  Moving only the records while leaving the mapper untouched would preserve a misleading ownership split; combining any
  other helpers would have mixed unrelated cleanup concerns.
- Preserved behavior: command handler return semantics, HTTP status mapping, projection readiness headers, validation
  problem mapping, API routes, response bodies, Swagger metadata, event contracts, outbox behavior, projections, EF
  mappings, migrations, and database schema remain unchanged.
- Deferred follow-on: `BudgetItemEligibilityService` and `BudgetItemValidationErrors` remain in
  `Application/Budgeting` because they support cross-command archived-budget-item correction rules across budgeting and
  transaction allocation paths. `BudgetLookup`, `EndpointValidation`, `CamelCaseStringEnumConverter`, and
  processing/failure persistence entity ownership remain separate review items.
- Remaining Step 12 work: continue reviewing one ownership concern at a time, especially shared endpoint helpers and
  remaining application-layer helpers, without broadening into domain or persistence redesign.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  `dotnet test` passed with 94 tests.
