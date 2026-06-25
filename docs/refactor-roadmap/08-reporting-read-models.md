# 08 - Reporting Read Models

## Goal

Organize reporting read models and projections by reporting capability.

## Scope

- Move snapshot read models and projection logic toward `Features/Reporting/Snapshots`.
- Keep read models beside the projection and query code they serve.
- Preserve projection-backed snapshot behavior.

## Out of scope

- Adding new reports such as monthly summary, cash flow, or category spend unless required later.
- Database schema changes.
- Moving Kafka consumer mechanics into feature code.

## Files likely affected

- `src/BudgetyTzar.Api/Application/Reporting/ProjectionModels.cs`
- `src/BudgetyTzar.Api/Application/Reporting/ReportingProjectionService.cs`
- `src/BudgetyTzar.Api/Application/Reporting/LedgerSnapshotCalculator.cs`
- `src/BudgetyTzar.Api/Features/Reporting/**`
- `src/BudgetyTzar.Api/Infrastructure/Persistence/BudgetDbContext.cs`

## Invariants to preserve

- Existing projection table names and EF mappings.
- Snapshot calculation results.
- Rebuild behavior from outbox.
- Projection readiness behavior.
- Current reporting API responses.

## Implementation checklist

- Split global projection models by capability/responsibility.
- Move snapshot projection models beside snapshot query/projection code.
- Keep EF `ToTable` mappings pinned to current names.
- Keep projection state models clear as projection state, not domain models.
- Avoid migrations unless the schema intentionally changes.

## Tests to run

- Reporting and budget snapshot tests.
- Projection processing tests.
- Persistence compatibility tests if EF mappings move.
- `dotnet test --no-restore`

## Completion notes

- Started with a narrow snapshot read-model organization increment by moving `BudgetSnapshotProjection` and
  `BudgetSnapshotItemProjection` out of the mixed `Application/Reporting/ProjectionModels.cs` file and into
  `Features/Reporting/Snapshots/SnapshotProjectionModels.cs`.
- Decision: keep the moved types in the existing `BudgetyTzar.Api.Application.Reporting` namespace so references,
  tests, EF model type names, and migration metadata remain stable. The change is file/folder ownership only.
- Decision: keep `BudgetDbContext` mappings unchanged and pinned to the existing `budget_snapshot` and
  `budget_snapshot_item` table names; no migration or database schema change was introduced.
- Decision: leave snapshot calculation, projection update behavior, projection readiness state, projection failures,
  audit failures, and API responses unchanged. This increment only separates the snapshot read-model entity pair from
  unrelated projection state types.
- Deferred follow-on: move the remaining projection state models beside the snapshot projection behavior when the
  ownership boundary is clear enough to keep projection mechanics separate from reporting read models.
- Deferred follow-on: separate projection processing/failure state from reporting read models in a later Step 08 or
  Step 09 increment, without changing retry, dead-letter, or readiness behavior.
- Remaining Step 08 work: continue organizing snapshot read models and projection logic by capability while preserving
  EF mappings, snapshot calculation results, rebuild behavior, and current reporting API responses.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` passed with 77 tests.
- Continued with a second narrow snapshot read-model organization increment by moving `BudgetItemProjectionState`,
  `BudgetAdjustmentProjectionState`, `TransactionProjectionState`, and `TransactionAllocationProjectionState` out of
  `Application/Reporting/ProjectionModels.cs` and into
  `Features/Reporting/Snapshots/SnapshotProjectionStateModels.cs`.
- Decision: treat these four classes as snapshot projection state, not domain models. They are maintained from
  budgeting and transaction events and feed snapshot recalculation, so they belong beside the snapshot projection
  read-model pair moved in the previous increment.
- Decision: keep the moved types in the existing `BudgetyTzar.Api.Application.Reporting` namespace so references, EF
  model type names, tests, and migration metadata remain stable. The change is file/folder ownership only.
- Decision: keep `BudgetDbContext` mappings unchanged and pinned to the existing `budget_item_projection_state`,
  `budget_adjustment_projection_state`, `transaction_projection_state`, and
  `transaction_allocation_projection_state` table names; no migration or database schema change was introduced.
- Decision: leave `ProcessedProjectionEvent`, `ProjectionEventFailure`, and `AuditEventFailure` in the existing mixed
  file for now because they describe processing/readiness/failure mechanics rather than snapshot read-model state.
- Deferred follow-on: separate projection processing and failure state in a later Step 08 or Step 09 increment once the
  infrastructure/projection-runner boundary is being addressed.
- Remaining Step 08 work: continue moving snapshot projection behavior toward `Features/Reporting/Snapshots` while
  keeping Kafka consumer mechanics in infrastructure and preserving rebuild, readiness, and snapshot API behavior.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` passed with 77 tests.
- Continued with a third narrow snapshot projection increment by moving `ReportingProjectionService` and
  `ProjectionApplyResult` from `Application/Reporting` to `Features/Reporting/Snapshots`.
- Decision: treat `ReportingProjectionService` as the concrete snapshot projection behavior because it applies typed
  budgeting and transaction event payloads, maintains the snapshot projection state tables, and recalculates the
  `budget_snapshot` and `budget_snapshot_item` read models.
- Decision: keep the moved service in the existing `BudgetyTzar.Api.Application.Reporting` namespace so DI registration,
  tests, infrastructure consumers, and public type references remain unchanged. The change is file/folder ownership only.
- Decision: leave Kafka consumer mechanics, projection processing state, projection failure state, and audit failure
  state outside the snapshot feature for now. This keeps Step 08 focused on concrete read-model/projection ownership and
  avoids pulling Step 09 infrastructure boundary work forward.
- Deferred follow-on: move or split `LedgerSnapshotCalculator` only in a future focused increment because it currently
  contains both API-facing snapshot response records and fallback calculation/query behavior.
- Remaining Step 08 work: continue organizing snapshot calculation/query code beside the snapshot capability while
  preserving projection-backed report behavior, current API responses, rebuild behavior, and readiness semantics.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` passed with 77 tests.
- Continued with a fourth narrow snapshot read-model increment by moving the API-facing `BudgetSnapshot` and
  `BudgetSnapshotItem` response records from `Application/Reporting/LedgerSnapshotCalculator.cs` to
  `Features/Reporting/Snapshots/BudgetSnapshotResponseModels.cs`.
- Decision: treat these records as the snapshot response read model served by the snapshot query and reused by the
  snapshot projection recalculation code. Keeping them beside the snapshot feature makes the remaining calculator file
  easier to split later without changing behavior.
- Decision: keep the moved records in the existing `BudgetyTzar.Api.Application.Reporting` namespace so tests,
  endpoint serialization, projection code, and public type references remain unchanged. The change is file/folder
  ownership only.
- Decision: leave `LedgerSnapshotCalculator` in `Application/Reporting` for now because moving the concrete fallback
  calculator is a separate behavior-adjacent increment and should be reviewed independently.
- Deferred follow-on: move or split `AuditEventDto` separately because it belongs to audit event listing rather than the
  snapshot read-model capability, and broader audit ownership is tracked by Step 10.
- Remaining Step 08 work: move or split the concrete snapshot fallback/projected calculator beside the snapshot
  capability while preserving current snapshot results and projection-backed report behavior.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` initially failed in the existing Kafka/audit dead-letter test with a transient SQLite active-statement
  error; the focused rerun passed, and a final full `dotnet test` passed with 77 tests.
- Continued with a fifth narrow snapshot calculation increment by moving `LedgerSnapshotCalculator` from
  `Application/Reporting` to `Features/Reporting/Snapshots`.
- Decision: treat `LedgerSnapshotCalculator` as concrete snapshot query/calculation behavior because it either
  calculates the snapshot directly from ledger tables or reads the projection-backed `budget_snapshot` read model for
  the snapshot endpoint.
- Decision: keep the moved calculator in the existing `BudgetyTzar.Api.Application.Reporting` namespace so endpoint
  references, tests, and public type references remain unchanged. The change is file/folder ownership only.
- Decision: move `AuditEventDto` into its own `Application/Reporting/AuditEventDto.cs` file without moving it to an
  audit feature. This keeps the snapshot calculator move coherent while preserving Step 10 as the place for broader
  audit ownership decisions.
- Deferred follow-on: audit event DTO/query ownership remains Step 10 work; projection processing/failure state remains
  Step 09 boundary work.
- Remaining Step 08 work: review whether any snapshot-specific query composition remains outside
  `Features/Reporting/Snapshots`, then close Step 08 or leave only explicitly deferred non-snapshot concerns.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` initially failed in the existing Kafka projection consumer timeout; the focused rerun passed, and a
  final full `dotnet test` passed with 77 tests.
- Continued with a sixth narrow snapshot query colocation increment by moving the snapshot endpoint mapping from
  `Features/Reporting/GetBudgetSnapshot/GetBudgetSnapshot.cs` to `Features/Reporting/Snapshots/GetBudgetSnapshot.cs`.
- Decision: treat `GetBudgetSnapshot` and `ProjectionPendingResponse` as part of the snapshot query capability because
  they compose the snapshot read model response and the projection-pending response for projection-backed reports.
- Decision: keep the endpoint on the existing `Endpoints` partial in the `BudgetyTzar.Api.Features` namespace so route
  composition, Swagger metadata, tests, and public type references remain unchanged. The change is file/folder ownership
  only.
- Decision: leave projection status and projection-events endpoints outside `Snapshots` because they are projection
  readiness/notification mechanics shared by reporting projection behavior, not the snapshot read model itself.
- Deferred follow-on: projection readiness/notification ownership remains Step 09 boundary work; audit event listing
  remains Step 10 work.
- Remaining Step 08 work: snapshot read models, snapshot projection state, snapshot projection behavior, calculator,
  and snapshot endpoint are now colocated under `Features/Reporting/Snapshots`; review for Step 08 closure or for any
  final documentation-only cleanup.
- Validation: `dotnet build BudgetyTzar.sln` hung with no output and was stopped, matching the known baseline caveat.
  `dotnet test` passed with 77 tests.
