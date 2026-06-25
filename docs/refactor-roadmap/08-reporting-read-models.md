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
