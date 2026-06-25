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
