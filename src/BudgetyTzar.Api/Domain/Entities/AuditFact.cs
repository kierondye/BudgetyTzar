using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace BudgetyTzar.Api.Domain.Entities;

public enum AuditAction
{
    BudgetCreated,
    BudgetRenamed,
    BudgetItemCreated,
    BudgetItemRenamed,
    BudgetItemPlannedAmountChanged,
    BudgetItemDeleted,
    TransactionCreated,
    TransactionDeleted,
    TransactionAllocationCreated,
    TransactionAllocationIdempotent,
    TransactionAllocationRemoved
}

public sealed record AuditFact
{
    private AuditFact(Guid id, AuditAction action, string? oldValue, string? newValue)
    {
        Id = id;
        Action = action;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public Guid Id { get; }

    public AuditAction Action { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    internal static AuditFact Create(AuditAction action, string? oldValue, string? newValue)
    {
        return new AuditFact(Guid.NewGuid(), action, oldValue, newValue);
    }
}

internal static class AuditValueSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(Budget budget)
    {
        var value = new BudgetAuditValue(
            budget.BudgetId,
            budget.Name.Value,
            budget.Currency.Value,
            budget.BudgetItems
                .Select(item => new BudgetItemAuditValue(
                    item.BudgetItemId,
                    item.Name.Value,
                    item.Kind.Value,
                    Money(item.PlannedAmount.Value)))
                .ToImmutableArray());

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string Serialize(Transaction transaction)
    {
        var value = new TransactionAuditValue(
            transaction.TransactionId,
            transaction.Type.Value,
            transaction.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Money(transaction.Amount.Value),
            transaction.Currency.Value);

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string Serialize(TransactionAllocation allocation)
    {
        var value = new TransactionAllocationAuditValue(
            allocation.TransactionId,
            allocation.BudgetItemId,
            Money(allocation.Amount.Value),
            allocation.Currency.Value);

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private sealed record BudgetAuditValue(
        Guid BudgetId,
        string Name,
        string Currency,
        ImmutableArray<BudgetItemAuditValue> BudgetItems);

    private sealed record BudgetItemAuditValue(
        Guid BudgetItemId,
        string Name,
        string Kind,
        string PlannedAmount);

    private sealed record TransactionAuditValue(
        Guid TransactionId,
        string Type,
        string TransactionDate,
        string Amount,
        string Currency);

    private sealed record TransactionAllocationAuditValue(
        Guid TransactionId,
        Guid BudgetItemId,
        string Amount,
        string Currency);
}
