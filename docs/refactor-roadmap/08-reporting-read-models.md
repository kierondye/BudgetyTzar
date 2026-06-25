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

- Not started.
