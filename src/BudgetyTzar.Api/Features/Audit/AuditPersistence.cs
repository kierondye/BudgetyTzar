using System.Globalization;
using System.Text.Json;
using BudgetyTzar.Api.Domain.Entities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BudgetyTzar.Api.Features.Audit;

public interface IAuditRecorder
{
    void Record(AuditEntry entry);
}

public interface IAuditOperationRunner
{
    T Execute<T>(Func<T> operation, Func<T, AuditEntry?> auditEntry);
}

public sealed class NoOpAuditRecorder : IAuditRecorder
{
    public void Record(AuditEntry entry)
    {
    }
}

public sealed class DefaultAuditOperationRunner(IAuditRecorder audit) : IAuditOperationRunner
{
    public T Execute<T>(Func<T> operation, Func<T, AuditEntry?> auditEntry)
    {
        var result = operation();
        var entry = auditEntry(result);

        if (entry is not null)
        {
            audit.Record(entry);
        }

        return result;
    }
}

public static class AuditBoundary
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditRecorder, NoOpAuditRecorder>();
        services.TryAddScoped<IAuditOperationRunner, DefaultAuditOperationRunner>();
        return services;
    }
}

public sealed record AuditEntry(
    string OperationName,
    string ResourceType,
    Guid ResourceId,
    string? BeforeStateJson,
    string? AfterStateJson)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AuditEntry BudgetCreated(Budget budget)
    {
        return new AuditEntry(
            "budget.create",
            "budget",
            budget.BudgetId,
            null,
            State(BudgetState.FromBudget(budget)));
    }

    public static AuditEntry BudgetRenamed(Budget before, Budget after)
    {
        return new AuditEntry(
            "budget.rename",
            "budget",
            after.BudgetId,
            State(BudgetState.FromBudget(before)),
            State(BudgetState.FromBudget(after)));
    }

    public static AuditEntry BudgetItemCreated(Budget budget, BudgetItem budgetItem)
    {
        return new AuditEntry(
            "budget_item.create",
            "budget_item",
            budgetItem.BudgetItemId,
            null,
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, budgetItem)));
    }

    public static AuditEntry BudgetItemRenamed(Budget budget, BudgetItem before, BudgetItem after)
    {
        return new AuditEntry(
            "budget_item.rename",
            "budget_item",
            after.BudgetItemId,
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, before)),
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, after)));
    }

    public static AuditEntry BudgetItemPlannedAmountChanged(Budget budget, BudgetItem before, BudgetItem after)
    {
        return new AuditEntry(
            "budget_item.change_planned_amount",
            "budget_item",
            after.BudgetItemId,
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, before)),
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, after)));
    }

    public static AuditEntry BudgetItemDeleted(Budget budget, BudgetItem budgetItem)
    {
        return new AuditEntry(
            "budget_item.delete",
            "budget_item",
            budgetItem.BudgetItemId,
            State(BudgetItemState.FromBudgetItem(budget.BudgetId, budgetItem)),
            null);
    }

    public static AuditEntry TransactionCreated(Transaction transaction)
    {
        return new AuditEntry(
            "transaction.create",
            "transaction",
            transaction.TransactionId,
            null,
            State(TransactionState.FromTransaction(transaction)));
    }

    public static AuditEntry TransactionDeleted(Transaction transaction)
    {
        return new AuditEntry(
            "transaction.delete",
            "transaction",
            transaction.TransactionId,
            State(TransactionState.FromTransaction(transaction)),
            null);
    }

    public static AuditEntry TransactionAllocationCreated(TransactionAllocation allocation)
    {
        return new AuditEntry(
            "transaction_allocation.create",
            "transaction_allocation",
            allocation.TransactionId,
            null,
            State(TransactionAllocationState.FromAllocation(allocation)));
    }

    public static AuditEntry TransactionAllocationIdempotent(TransactionAllocation allocation)
    {
        var state = State(TransactionAllocationState.FromAllocation(allocation));
        return new AuditEntry(
            "transaction_allocation.idempotent",
            "transaction_allocation",
            allocation.TransactionId,
            state,
            state);
    }

    public static AuditEntry TransactionAllocationRemoved(TransactionAllocation allocation)
    {
        return new AuditEntry(
            "transaction_allocation.remove",
            "transaction_allocation",
            allocation.TransactionId,
            State(TransactionAllocationState.FromAllocation(allocation)),
            null);
    }

    private static string State(object state)
    {
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    private sealed record BudgetState(string Name, string Currency)
    {
        public static BudgetState FromBudget(Budget budget)
        {
            return new BudgetState(budget.Name.Value, budget.Currency.Value);
        }
    }

    private sealed record BudgetItemState(Guid BudgetId, string Name, string Kind, string PlannedAmount)
    {
        public static BudgetItemState FromBudgetItem(Guid budgetId, BudgetItem budgetItem)
        {
            return new BudgetItemState(
                budgetId,
                budgetItem.Name.Value,
                budgetItem.Kind.Value,
                Money(budgetItem.PlannedAmount.Value));
        }
    }

    private sealed record TransactionState(string Type, string TransactionDate, string Amount, string Currency)
    {
        public static TransactionState FromTransaction(Transaction transaction)
        {
            return new TransactionState(
                transaction.Type.Value,
                transaction.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money(transaction.Amount.Value),
                transaction.Currency.Value);
        }
    }

    private sealed record TransactionAllocationState(Guid BudgetItemId, string Amount, string Currency)
    {
        public static TransactionAllocationState FromAllocation(TransactionAllocation allocation)
        {
            return new TransactionAllocationState(
                allocation.BudgetItemId,
                Money(allocation.Amount.Value),
                allocation.Currency.Value);
        }
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
