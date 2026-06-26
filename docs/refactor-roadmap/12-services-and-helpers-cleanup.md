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
- Continued with a serialization-helper ownership cleanup by moving `CamelCaseStringEnumConverter` from
  `Features/Shared` to `Infrastructure/Serialization`.
- Decision: treat the converter as infrastructure serialization plumbing because it configures JSON enum formatting for
  HTTP serialization and enum attributes. It is not feature behavior, domain behavior, or persistence state.
- Decision: keep the converter in the existing `BudgetyTzar.Api` namespace and keep the implementation unchanged so
  `Program`, enum attributes, API JSON behavior, and tests continue resolving the same type without contract churn.
- Grouping rationale: this increment intentionally moves only the general enum converter. `EventSerialization` remains
  under `Infrastructure/Events` because it is specifically event-envelope/payload serialization, while the converter is
  general API/domain enum serialization. Combining them would mix related but distinct serialization concerns.
- Preserved behavior: enum JSON values remain camelCase, integer enum values remain disallowed, API routes and response
  shapes remain unchanged, event contracts and schemas remain unchanged, projections/audit/snapshots remain unchanged,
  and EF mappings, migrations, and database schema remain unchanged.
- Deferred follow-on: `BudgetLookup` and `EndpointValidation` remain shared endpoint helpers pending separate ownership
  review. Processing/failure persistence entities remain deferred because moving them could affect EF model identity and
  migration snapshots.
- Remaining Step 12 work: continue reviewing shared endpoint helpers and remaining application-layer helpers one
  ownership concern at a time.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  `dotnet test` passed with 94 tests.
- Continued with a budget-item eligibility ownership cleanup by moving `BudgetItemEligibilityService` and
  `BudgetItemValidationErrors` from `Application/Budgeting` to `Features/Budgeting/BudgetItems`.
- Decision: treat budget-item eligibility lookup and archived-item validation error construction as budgeting feature
  helpers. Transaction allocation still calls the helper, but the protected concept is budgeting-owned: whether a
  budget item exists in the budget and can accept activity on a command date.
- Decision: keep `BudgetItem.CanAcceptActivityOn` as the domain rule owner, keep the eligibility service as concrete
  feature command orchestration support, and keep EF query/persistence plumbing unchanged through `BudgetDbContext`.
- Decision: preserve the existing scoped service lifetime, method names, no-tracking lookup behavior, error dictionary
  key, and validation message. No repository, mediator, generic service framework, or cross-feature abstraction was
  introduced.
- Grouping rationale: the service and validation-error helper are one archived-budget-item eligibility concern used by
  adjustment, reallocation, and transaction allocation command paths. Moving them together avoids leaving half of the
  same command helper concern in `Application/Budgeting`.
- Preserved behavior: adjustment, reallocation, and transaction allocation routes, status codes, response bodies,
  Swagger metadata, archived-item validation message, event contracts, outbox behavior, projections, audit behavior,
  snapshots, EF mappings, migrations, and database schema remain unchanged.
- Deferred follow-on: `BudgetLookup` and `EndpointValidation` remain shared endpoint helpers pending separate review.
  Projection/audit processing and failure persistence entities remain in `Application/Reporting` to avoid EF model
  identity and migration snapshot churn.
- Remaining Step 12 work: review the remaining shared endpoint helpers and reporting/audit persistence entity ownership
  boundaries without broadening into persistence redesign.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  `dotnet test` compiled but failed on the known
  `KafkaProjectionConsumerTests.ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents`
  SQLite active-statement dead-letter transient; the focused rerun of that failing test hit the same transient, and the
  final full `dotnet test` again failed only on that same test. Focused touched-path validation passed with
  `dotnet test --filter "FullyQualifiedName~BudgetAdjustmentsTests|FullyQualifiedName~BudgetReallocationsTests|FullyQualifiedName~TransactionAllocationsTests|FullyQualifiedName~BudgetItemTests"`
  (13 tests).
- Continued with a projection-runner persistence ownership cleanup by moving `ProcessedProjectionEvent`,
  `ProjectionProcessingStatus`, `ProjectionEventFailure`, `ProjectionFailureCategory`, and `ProjectionFailureStatus`
  from `Application/Reporting` to `Infrastructure/Events`.
- Decision: treat projection processing/checkpoint/readiness rows and projection failure rows as infrastructure event
  runner persistence state. Reporting endpoints may read readiness state, but infrastructure owns claiming, completion,
  failure persistence, retry/dead-letter metadata, and rebuild cleanup.
- Decision: keep the moved types in the existing `BudgetyTzar.Api.Application.Reporting` namespace so EF model identity,
  migration snapshots, tests, endpoint references, and infrastructure store references remain unchanged. This is
  file/folder ownership only.
- Grouping rationale: `ProcessedProjectionEvent` and `ProjectionEventFailure` are the two persistence entities owned by
  the reporting projection runner boundary. Moving them together clarifies that processing lifecycle and projection
  failure persistence are one infrastructure concern without mixing in the separate audit failure entity.
- Preserved behavior: projection idempotency, processing leases/checkpoints, readiness status semantics, failure and
  dead-letter persistence values, rebuild cleanup, API responses, event contracts, Kafka topics, outbox behavior, EF
  mappings, migrations, database schema, projections, audit behavior, and snapshots remain unchanged.
- Deferred follow-on: `AuditEventFailure` remains in `Application/Reporting` for a separate audit-runner persistence
  ownership increment. `BudgetLookup` and `EndpointValidation` remain shared endpoint helpers pending separate review.
- Remaining Step 12 work: review audit failure persistence ownership and the remaining shared endpoint helpers without
  changing EF model identity, API contracts, or runtime behavior.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  The first `dotnet test` compiled but failed on the known
  `KafkaProjectionConsumerTests.ReportingProjectionConsumerDeadLettersPoisonEventAndContinuesWithLaterEvents` SQLite
  dead-letter transient. The focused rerun of that failing test passed, and the final full `dotnet test` passed with
  94 tests.
- Continued with an audit-runner persistence ownership cleanup by moving `AuditEventFailure`,
  `AuditFailureCategory`, and `AuditFailureStatus` from `Application/Reporting` to `Infrastructure/Events`.
- Decision: treat audit failure rows as infrastructure event consumer persistence state. The audit feature owns audit
  projection/query behavior, while infrastructure owns consumer transport, retry/dead-letter flow, failure persistence,
  raw-event metadata, and operational storage.
- Decision: keep the moved types in the existing `BudgetyTzar.Api.Application.Reporting` namespace so EF model identity,
  migration snapshots, tests, infrastructure store references, and consumer references remain unchanged. This is
  file/folder ownership only.
- Grouping rationale: `AuditEventFailure` is the audit counterpart to the projection failure entity moved in the
  previous increment. Moving it separately keeps audit-runner persistence distinct from reporting projection-runner
  persistence while completing the remaining `Application/Reporting` cleanup.
- Preserved behavior: audit failure categories/statuses and stored string values, retry/dead-letter persistence,
  dead-letter payload behavior, audit projection/query behavior, event contracts, Kafka topics, outbox behavior, EF
  mappings, migrations, database schema, projections, snapshots, and API responses remain unchanged.
- Deferred follow-on: `BudgetLookup` and `EndpointValidation` remain shared endpoint helpers pending separate review.
  `Application` no longer contains files after this increment; keep it absent unless a future application-layer concept
  has clear ownership.
- Remaining Step 12 work: review the remaining shared endpoint helpers and decide whether Step 12 can close without
  broadening into speculative cleanup.
- Validation: `dotnet build BudgetyTzar.sln` hung silently and was stopped after matching the known roadmap caveat.
  `dotnet test` passed with 94 tests.
