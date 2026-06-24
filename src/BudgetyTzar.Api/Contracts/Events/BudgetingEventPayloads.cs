namespace BudgetyTzar.Api.Contracts.Events;

public sealed record BudgetCreatedPayload(
    Guid BudgetId,
    string Name,
    string Currency);

public sealed record BudgetItemCreatedPayload(
    Guid BudgetId,
    Guid BudgetItemId,
    string Name);

public sealed record BudgetItemArchivedPayload(
    Guid BudgetId,
    Guid BudgetItemId,
    string Name,
    DateTimeOffset ArchivedAt);

public sealed record BudgetAdjustmentRecordedPayload(
    Guid BudgetAdjustmentId,
    Guid BudgetId,
    Guid BudgetItemId,
    decimal Amount,
    BudgetAdjustmentType Direction,
    DateOnly Date,
    string? Notes);

public sealed record BudgetReallocationRecordedPayload(
    Guid BudgetReallocationId,
    Guid BudgetId,
    DateOnly Date,
    string? Notes,
    IReadOnlyList<BudgetReallocationAdjustmentPayload> Adjustments);

public sealed record BudgetReallocationAdjustmentPayload(
    Guid BudgetItemId,
    decimal Amount,
    BudgetAdjustmentType Direction);
