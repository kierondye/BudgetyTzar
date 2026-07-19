using System.Globalization;
using System.Text.Json;
using BudgetyTzar.Api.Domain.Entities;

namespace BudgetyTzar.Api.Features.Audit;

public static class AuditBoundary
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
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
        return BudgetCreated(budget.BudgetId, budget.Name.Value, budget.Currency.Value);
    }

    public static AuditEntry BudgetCreated(Guid budgetId, string name, string currency)
    {
        return new AuditEntry(
            "budget.create",
            "budget",
            budgetId,
            null,
            State(new BudgetState(name, currency)));
    }

    public static AuditEntry BudgetRenamed(Budget before, Budget after)
    {
        return BudgetRenamed(
            after.BudgetId,
            before.Name.Value,
            before.Currency.Value,
            after.Name.Value,
            after.Currency.Value);
    }

    public static AuditEntry BudgetRenamed(
        Guid budgetId,
        string beforeName,
        string beforeCurrency,
        string afterName,
        string afterCurrency)
    {
        return new AuditEntry(
            "budget.rename",
            "budget",
            budgetId,
            State(new BudgetState(beforeName, beforeCurrency)),
            State(new BudgetState(afterName, afterCurrency)));
    }

    public static AuditEntry BudgetItemCreated(Budget budget, BudgetItem budgetItem)
    {
        return BudgetItemCreated(
            budget.BudgetId,
            budgetItem.BudgetItemId,
            budgetItem.Name.Value,
            budgetItem.Kind.Value,
            budgetItem.PlannedAmount.Value);
    }

    public static AuditEntry BudgetItemCreated(
        Guid budgetId,
        Guid budgetItemId,
        string name,
        string kind,
        decimal plannedAmount)
    {
        return new AuditEntry(
            "budget_item.create",
            "budget_item",
            budgetItemId,
            null,
            State(new BudgetItemState(budgetId, name, kind, Money(plannedAmount))));
    }

    public static AuditEntry BudgetItemRenamed(Budget budget, BudgetItem before, BudgetItem after)
    {
        return BudgetItemRenamed(
            budget.BudgetId,
            after.BudgetItemId,
            before.Name.Value,
            before.Kind.Value,
            before.PlannedAmount.Value,
            after.Name.Value,
            after.Kind.Value,
            after.PlannedAmount.Value);
    }

    public static AuditEntry BudgetItemRenamed(
        Guid budgetId,
        Guid budgetItemId,
        string beforeName,
        string beforeKind,
        decimal beforePlannedAmount,
        string afterName,
        string afterKind,
        decimal afterPlannedAmount)
    {
        return new AuditEntry(
            "budget_item.rename",
            "budget_item",
            budgetItemId,
            State(new BudgetItemState(budgetId, beforeName, beforeKind, Money(beforePlannedAmount))),
            State(new BudgetItemState(budgetId, afterName, afterKind, Money(afterPlannedAmount))));
    }

    public static AuditEntry BudgetItemPlannedAmountChanged(Budget budget, BudgetItem before, BudgetItem after)
    {
        return BudgetItemPlannedAmountChanged(
            budget.BudgetId,
            after.BudgetItemId,
            before.Name.Value,
            before.Kind.Value,
            before.PlannedAmount.Value,
            after.Name.Value,
            after.Kind.Value,
            after.PlannedAmount.Value);
    }

    public static AuditEntry BudgetItemPlannedAmountChanged(
        Guid budgetId,
        Guid budgetItemId,
        string beforeName,
        string beforeKind,
        decimal beforePlannedAmount,
        string afterName,
        string afterKind,
        decimal afterPlannedAmount)
    {
        return new AuditEntry(
            "budget_item.change_planned_amount",
            "budget_item",
            budgetItemId,
            State(new BudgetItemState(budgetId, beforeName, beforeKind, Money(beforePlannedAmount))),
            State(new BudgetItemState(budgetId, afterName, afterKind, Money(afterPlannedAmount))));
    }

    public static AuditEntry BudgetItemDeleted(Budget budget, BudgetItem budgetItem)
    {
        return BudgetItemDeleted(
            budget.BudgetId,
            budgetItem.BudgetItemId,
            budgetItem.Name.Value,
            budgetItem.Kind.Value,
            budgetItem.PlannedAmount.Value);
    }

    public static AuditEntry BudgetItemDeleted(
        Guid budgetId,
        Guid budgetItemId,
        string name,
        string kind,
        decimal plannedAmount)
    {
        return new AuditEntry(
            "budget_item.delete",
            "budget_item",
            budgetItemId,
            State(new BudgetItemState(budgetId, name, kind, Money(plannedAmount))),
            null);
    }

    public static AuditEntry TransactionCreated(Transaction transaction)
    {
        return TransactionCreated(
            transaction.TransactionId,
            transaction.Type.Value,
            transaction.TransactionDate,
            transaction.Amount.Value,
            transaction.Currency.Value);
    }

    public static AuditEntry TransactionCreated(
        Guid transactionId,
        string type,
        DateOnly transactionDate,
        decimal amount,
        string currency)
    {
        return new AuditEntry(
            "transaction.create",
            "transaction",
            transactionId,
            null,
            State(new TransactionState(
                type,
                transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money(amount),
                currency)));
    }

    public static AuditEntry TransactionDeleted(Transaction transaction)
    {
        return TransactionDeleted(
            transaction.TransactionId,
            transaction.Type.Value,
            transaction.TransactionDate,
            transaction.Amount.Value,
            transaction.Currency.Value);
    }

    public static AuditEntry TransactionDeleted(
        Guid transactionId,
        string type,
        DateOnly transactionDate,
        decimal amount,
        string currency)
    {
        return new AuditEntry(
            "transaction.delete",
            "transaction",
            transactionId,
            State(new TransactionState(
                type,
                transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money(amount),
                currency)),
            null);
    }

    public static AuditEntry TransactionAllocationCreated(TransactionAllocation allocation)
    {
        return TransactionAllocationCreated(
            allocation.TransactionId,
            allocation.BudgetItemId,
            allocation.Amount.Value,
            allocation.Currency.Value);
    }

    public static AuditEntry TransactionAllocationCreated(
        Guid transactionId,
        Guid budgetItemId,
        decimal amount,
        string currency)
    {
        return new AuditEntry(
            "transaction_allocation.create",
            "transaction_allocation",
            transactionId,
            null,
            State(new TransactionAllocationState(budgetItemId, Money(amount), currency)));
    }

    public static AuditEntry TransactionAllocationIdempotent(TransactionAllocation allocation)
    {
        return TransactionAllocationIdempotent(
            allocation.TransactionId,
            allocation.BudgetItemId,
            allocation.Amount.Value,
            allocation.Currency.Value);
    }

    public static AuditEntry TransactionAllocationIdempotent(
        Guid transactionId,
        Guid budgetItemId,
        decimal amount,
        string currency)
    {
        var state = State(new TransactionAllocationState(budgetItemId, Money(amount), currency));
        return new AuditEntry(
            "transaction_allocation.idempotent",
            "transaction_allocation",
            transactionId,
            state,
            state);
    }

    public static AuditEntry TransactionAllocationRemoved(TransactionAllocation allocation)
    {
        return TransactionAllocationRemoved(
            allocation.TransactionId,
            allocation.BudgetItemId,
            allocation.Amount.Value,
            allocation.Currency.Value);
    }

    public static AuditEntry TransactionAllocationRemoved(
        Guid transactionId,
        Guid budgetItemId,
        decimal amount,
        string currency)
    {
        return new AuditEntry(
            "transaction_allocation.remove",
            "transaction_allocation",
            transactionId,
            State(new TransactionAllocationState(budgetItemId, Money(amount), currency)),
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
