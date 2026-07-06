using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Transactions;

namespace BudgetyTzar.Api.Features.Reporting;

public sealed class BudgetSummaryService(
    BudgetStore budgetStore,
    TransactionStore transactionStore,
    TransactionAllocationStore allocationStore)
{
    public GetBudgetSummaryResult Get(Guid budgetId)
    {
        var budget = budgetStore.Get(budgetId);

        if (budget is null)
        {
            return new GetBudgetSummaryResult.NotFound();
        }

        var transactionsById = transactionStore.GetAll()
            .ToDictionary(transaction => transaction.TransactionId);
        var allocationsByBudgetItemId = allocationStore.GetAll()
            .GroupBy(allocation => allocation.BudgetItemId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var funding = CreateSection(budget, BudgetItemKind.Funding, transactionsById, allocationsByBudgetItemId);
        var consumption = CreateSection(budget, BudgetItemKind.Consumption, transactionsById, allocationsByBudgetItemId);
        var overall = new BudgetSummaryOverall(
            funding.TotalPlannedAmount - consumption.TotalPlannedAmount,
            funding.TotalActualAmount - consumption.TotalActualAmount);

        var summary = new BudgetSummary(
            budget.BudgetId,
            budget.Name,
            budget.Currency.Value,
            funding,
            consumption,
            overall);

        return new GetBudgetSummaryResult.Found(summary);
    }

    private static BudgetSummarySection CreateSection(
        Budget budget,
        BudgetItemKind kind,
        IReadOnlyDictionary<Guid, Transaction> transactionsById,
        IReadOnlyDictionary<Guid, List<TransactionAllocation>> allocationsByBudgetItemId)
    {
        var items = budget.BudgetItems
            .Where(budgetItem => budgetItem.Kind == kind)
            .Select(budgetItem => CreateItem(budgetItem, transactionsById, allocationsByBudgetItemId))
            .ToList();

        var totalPlannedAmount = items.Sum(item => item.PlannedAmount);
        var totalActualAmount = items.Sum(item => item.ActualAmount);

        return new BudgetSummarySection(
            items,
            totalPlannedAmount,
            totalActualAmount,
            totalPlannedAmount - totalActualAmount);
    }

    private static BudgetSummaryItem CreateItem(
        BudgetItem budgetItem,
        IReadOnlyDictionary<Guid, Transaction> transactionsById,
        IReadOnlyDictionary<Guid, List<TransactionAllocation>> allocationsByBudgetItemId)
    {
        var actualAmount = allocationsByBudgetItemId.TryGetValue(budgetItem.BudgetItemId, out var allocations)
            ? allocations.Sum(allocation => CalculateActualContribution(budgetItem.Kind, allocation, transactionsById))
            : 0.00m;

        return new BudgetSummaryItem(
            budgetItem.BudgetItemId,
            budgetItem.Name,
            budgetItem.PlannedAmount.Value,
            actualAmount,
            budgetItem.PlannedAmount.Value - actualAmount);
    }

    private static decimal CalculateActualContribution(
        BudgetItemKind budgetItemKind,
        TransactionAllocation allocation,
        IReadOnlyDictionary<Guid, Transaction> transactionsById)
    {
        if (!transactionsById.TryGetValue(allocation.TransactionId, out var transaction))
        {
            return 0.00m;
        }

        if (budgetItemKind == BudgetItemKind.Funding)
        {
            return transaction.Type == TransactionType.Credit
                ? transaction.Amount.Value
                : -transaction.Amount.Value;
        }

        return transaction.Type == TransactionType.Debit
            ? transaction.Amount.Value
            : -transaction.Amount.Value;
    }
}
