using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Tests.Support.Persistence;

public abstract class RepositoryContractTestBase
{
    protected abstract ValueTask<RepositoryContractContext> CreateContextAsync();

    protected static Budget CreateBudget(string name = "UK", string currency = "GBP")
    {
        return Assert.IsType<CreateBudgetResult.Created>(
            Budget.Create(Guid.NewGuid(), Name(name), Currency(currency))).Budget;
    }

    protected static Budget CreateBudget(params (Guid BudgetItemId, string Name)[] budgetItems)
    {
        var budget = CreateBudget();

        foreach (var budgetItem in budgetItems)
        {
            budget = Assert.IsType<AddBudgetItemResult.Added>(
                budget.AddBudgetItem(
                    budgetItem.BudgetItemId,
                    Name(budgetItem.Name),
                    BudgetItemKind.Consumption,
                    Money("400.00"))).Budget;
        }

        return budget;
    }

    protected static Transaction CreateTransaction(string description = "Groceries", string amount = "42.50")
    {
        return Assert.IsType<RecordTransactionResult.Recorded>(
            Transaction.Record(
                Guid.NewGuid(),
                description,
                TransactionType.Debit,
                new DateOnly(2026, 7, 2),
                Money(amount),
                Currency("GBP"))).Transaction;
    }

    protected static TransactionAllocation CreateAllocation(Transaction transaction, Guid budgetItemId)
    {
        return Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, budgetItemId)).Allocation;
    }

    protected static NormalizedName Name(string value)
    {
        return NormalizedName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException("Invalid test name.");
    }

    protected static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Invalid test currency.");
    }

    protected static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var amount)
            ? amount!
            : throw new InvalidOperationException("Invalid test amount.");
    }
}
