namespace BudgetyTzar.Api.Contracts.Events;

public sealed record TransactionManuallyCreatedPayload(
    Guid TransactionId,
    Guid BudgetId,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes,
    bool IsIgnored);

public sealed record TransactionEditedPayload(
    Guid TransactionId,
    Guid BudgetId,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes,
    bool IsIgnored);

public sealed record TransactionIgnoredPayload(
    Guid TransactionId,
    Guid BudgetId,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    TransactionDirection Direction,
    string? SourceAccount,
    string? ExternalReference,
    string? Notes,
    bool IsIgnored);

public sealed record TransactionAllocationsReplacedPayload(
    Guid TransactionId,
    Guid BudgetId,
    decimal TransactionAmount,
    IReadOnlyList<TransactionAllocationPayload> Allocations);

public sealed record TransactionAllocationsClearedPayload(
    Guid TransactionId,
    Guid BudgetId,
    IReadOnlyList<TransactionAllocationPayload> ClearedAllocations);

public sealed record TransactionAllocationPayload(
    Guid BudgetItemId,
    decimal Amount,
    string? Notes);
