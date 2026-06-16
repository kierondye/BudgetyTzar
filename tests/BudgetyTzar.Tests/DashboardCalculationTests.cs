using BudgetyTzar.Api;
using BudgetyTzar.Api.Features;
using Xunit;

namespace BudgetyTzar.Tests;

public sealed class DashboardCalculationTests
{
    private const string Currency = "GBP";

    [Fact]
    public void BudgetReallocationsAdjustAvailableBudgetButDoNotAlterActualSpending()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = DebitLine(budget, "Groceries");
        var dining = DebitLine(budget, "Dining out");
        var grocerySpend = Transaction(budget, period.StartDate, 30m, TransactionDirection.Debit);

        var withoutReallocation = DashboardCalculator.Calculate(
            period,
            [period],
            [groceries, dining],
            [Allocation(period, groceries, 100m), Allocation(period, dining, 75m)],
            [grocerySpend],
            [Assignment(grocerySpend, groceries, 30m)],
            []);

        var withReallocation = DashboardCalculator.Calculate(
            period,
            [period],
            [groceries, dining],
            [Allocation(period, groceries, 100m), Allocation(period, dining, 75m)],
            [grocerySpend],
            [Assignment(grocerySpend, groceries, 30m)],
            [Reallocation(period, dining, groceries, 25m)]);

        Assert.Equal(30m, withoutReallocation.Line(groceries).ActualAmount);
        Assert.Equal(30m, withReallocation.Line(groceries).ActualAmount);
        Assert.Equal(70m, withoutReallocation.Line(groceries).ClosingBalance);
        Assert.Equal(95m, withReallocation.Line(groceries).ClosingBalance);
        Assert.Equal(175m, withoutReallocation.PlannedDebit);
        Assert.Equal(175m, withReallocation.PlannedDebit);
    }

    [Fact]
    public void CreditBudgetLinesReportExpectedAgainstActualCredit()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var salary = CreditLine(budget, "Salary");
        var groceries = DebitLine(budget, "Groceries");
        var paycheck = Transaction(budget, period.StartDate, 2_950m, TransactionDirection.Credit);
        var food = Transaction(budget, period.StartDate, 40m, TransactionDirection.Debit);

        var summary = DashboardCalculator.Calculate(
            period,
            [period],
            [salary, groceries],
            [Allocation(period, salary, 3_000m), Allocation(period, groceries, 150m)],
            [paycheck, food],
            [Assignment(paycheck, salary, 2_950m), Assignment(food, groceries, 40m)],
            []);

        Assert.Equal(3_000m, summary.PlannedCredit);
        Assert.Equal(2_950m, summary.ActualCredit);
        Assert.Equal(-50m, summary.CreditVariance);
        Assert.Equal(150m, summary.PlannedDebit);
        Assert.Equal(40m, summary.ActualDebit);
        Assert.Equal(110m, summary.DebitRemaining);
    }

    [Fact]
    public void CreditRefundAssignedToDebitLineReducesActualSpending()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = DebitLine(budget, "Groceries");
        var refund = Transaction(budget, period.StartDate, 25m, TransactionDirection.Credit);

        var summary = DashboardCalculator.Calculate(
            period,
            [period],
            [groceries],
            [Allocation(period, groceries, 100m)],
            [refund],
            [Assignment(refund, groceries, 25m)],
            []);

        Assert.Equal(-25m, summary.Line(groceries).ActualAmount);
        Assert.Equal(-25m, summary.ActualDebit);
        Assert.Equal(125m, summary.DebitRemaining);
        Assert.Equal(125m, summary.DebitVariance);
        Assert.False(summary.Line(groceries).IsOverBudget);
    }

    [Fact]
    public void DebitReversalAssignedToCreditLineReducesActualCredit()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var salary = CreditLine(budget, "Salary");
        var reversal = Transaction(budget, period.StartDate, 100m, TransactionDirection.Debit);

        var summary = DashboardCalculator.Calculate(
            period,
            [period],
            [salary],
            [Allocation(period, salary, 1_000m)],
            [reversal],
            [Assignment(reversal, salary, 100m)],
            []);

        Assert.Equal(-100m, summary.Line(salary).ActualAmount);
        Assert.Equal(-100m, summary.ActualCredit);
        Assert.Equal(-1_100m, summary.CreditVariance);
        Assert.Equal(-1_100m, summary.Line(salary).ClosingBalance);
    }

    [Fact]
    public void MixedSplitAssignmentsUseSignedTotalsForEachBudgetLineDirection()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = DebitLine(budget, "Groceries");
        var salary = CreditLine(budget, "Salary");
        var cardSpend = Transaction(budget, period.StartDate, 100m, TransactionDirection.Debit);
        var refund = Transaction(budget, period.StartDate, 40m, TransactionDirection.Credit);

        var summary = DashboardCalculator.Calculate(
            period,
            [period],
            [groceries, salary],
            [Allocation(period, groceries, 80m), Allocation(period, salary, 50m)],
            [cardSpend, refund],
            [
                Assignment(cardSpend, groceries, 70m),
                Assignment(cardSpend, salary, 30m),
                Assignment(refund, groceries, 25m),
                Assignment(refund, salary, 15m)
            ],
            []);

        Assert.Equal(45m, summary.Line(groceries).ActualAmount);
        Assert.Equal(35m, summary.Line(groceries).ClosingBalance);
        Assert.Equal(-15m, summary.Line(salary).ActualAmount);
        Assert.Equal(-65m, summary.Line(salary).ClosingBalance);
        Assert.Equal(45m, summary.ActualDebit);
        Assert.Equal(-15m, summary.ActualCredit);
        Assert.Equal(0m, summary.PartiallyAssignedDebitTotal);
        Assert.Equal(0m, summary.PartiallyAssignedCreditTotal);
    }

    [Fact]
    public void UnassignedAndPartiallyAssignedTransactionsAreVisibleInPeriodSummary()
    {
        var budget = Budget();
        var period = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = DebitLine(budget, "Groceries");
        var salary = CreditLine(budget, "Salary");
        var unassignedDebit = Transaction(budget, period.StartDate, 25m, TransactionDirection.Debit);
        var partialDebit = Transaction(budget, period.StartDate, 80m, TransactionDirection.Debit);
        var unassignedCredit = Transaction(budget, period.StartDate, 100m, TransactionDirection.Credit);
        var partialCredit = Transaction(budget, period.StartDate, 200m, TransactionDirection.Credit);

        var summary = DashboardCalculator.Calculate(
            period,
            [period],
            [groceries, salary],
            [Allocation(period, groceries, 150m), Allocation(period, salary, 200m)],
            [unassignedDebit, partialDebit, unassignedCredit, partialCredit],
            [Assignment(partialDebit, groceries, 50m), Assignment(partialCredit, salary, 125m)],
            []);

        Assert.Equal(25m, summary.UnassignedDebitTotal);
        Assert.Equal(100m, summary.UnassignedCreditTotal);
        Assert.Equal(30m, summary.PartiallyAssignedDebitTotal);
        Assert.Equal(75m, summary.PartiallyAssignedCreditTotal);
        Assert.Equal(50m, summary.ActualDebit);
        Assert.Equal(125m, summary.ActualCredit);
    }

    [Fact]
    public void PeriodResetBudgetLinesDoNotCarryBalancesForward()
    {
        var budget = Budget();
        var may = Period(budget, "May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var june = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var groceries = DebitLine(budget, "Groceries", BudgetLineRolloverType.PeriodReset);
        var maySpend = Transaction(budget, may.StartDate, 40m, TransactionDirection.Debit);
        var juneSpend = Transaction(budget, june.StartDate, 20m, TransactionDirection.Debit);

        var summary = DashboardCalculator.Calculate(
            june,
            [may, june],
            [groceries],
            [Allocation(may, groceries, 100m), Allocation(june, groceries, 50m)],
            [maySpend, juneSpend],
            [Assignment(maySpend, groceries, 40m), Assignment(juneSpend, groceries, 20m)],
            []);

        Assert.Equal(0m, summary.Line(groceries).OpeningBalance);
        Assert.Equal(30m, summary.Line(groceries).ClosingBalance);
    }

    [Fact]
    public void CumulativeBudgetLinesCarryPreviousClosingBalanceForward()
    {
        var budget = Budget();
        var may = Period(budget, "May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var june = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var holiday = DebitLine(budget, "Holiday fund", BudgetLineRolloverType.Cumulative);
        var maySpend = Transaction(budget, may.StartDate, 40m, TransactionDirection.Debit);
        var juneSpend = Transaction(budget, june.StartDate, 20m, TransactionDirection.Debit);

        var summary = DashboardCalculator.Calculate(
            june,
            [may, june],
            [holiday],
            [Allocation(may, holiday, 100m), Allocation(june, holiday, 50m)],
            [maySpend, juneSpend],
            [Assignment(maySpend, holiday, 40m), Assignment(juneSpend, holiday, 20m)],
            []);

        Assert.Equal(60m, summary.Line(holiday).OpeningBalance);
        Assert.Equal(90m, summary.Line(holiday).ClosingBalance);
    }

    [Fact]
    public void CumulativeDebitBudgetLinesCarrySignedBalancesForward()
    {
        var budget = Budget();
        var may = Period(budget, "May 2026", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        var june = Period(budget, "June 2026", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var holiday = DebitLine(budget, "Holiday fund", BudgetLineRolloverType.Cumulative);
        var maySpend = Transaction(budget, may.StartDate, 70m, TransactionDirection.Debit);
        var mayRefund = Transaction(budget, may.StartDate, 20m, TransactionDirection.Credit);
        var juneRefund = Transaction(budget, june.StartDate, 10m, TransactionDirection.Credit);

        var summary = DashboardCalculator.Calculate(
            june,
            [may, june],
            [holiday],
            [Allocation(may, holiday, 100m), Allocation(june, holiday, 30m)],
            [maySpend, mayRefund, juneRefund],
            [
                Assignment(maySpend, holiday, 70m),
                Assignment(mayRefund, holiday, 20m),
                Assignment(juneRefund, holiday, 10m)
            ],
            []);

        Assert.Equal(50m, summary.Line(holiday).OpeningBalance);
        Assert.Equal(-10m, summary.Line(holiday).ActualAmount);
        Assert.Equal(90m, summary.Line(holiday).ClosingBalance);
        Assert.Equal(-10m, summary.ActualDebit);
        Assert.Equal(90m, summary.DebitRemaining);
    }

    private static Budget Budget() => new()
    {
        Name = "Personal Budget",
        Currency = Currency
    };

    private static BudgetPeriod Period(Budget budget, string name, DateOnly start, DateOnly end) => new()
    {
        BudgetId = budget.Id,
        Name = name,
        StartDate = start,
        EndDate = end
    };

    private static BudgetLine DebitLine(
        Budget budget,
        string name,
        BudgetLineRolloverType rolloverType = BudgetLineRolloverType.PeriodReset) => new()
    {
        BudgetId = budget.Id,
        Name = name,
        Direction = BudgetLineDirection.Debit,
        RolloverType = rolloverType
    };

    private static BudgetLine CreditLine(Budget budget, string name) => new()
    {
        BudgetId = budget.Id,
        Name = name,
        Direction = BudgetLineDirection.Credit,
        RolloverType = BudgetLineRolloverType.PeriodReset
    };

    private static BudgetLineAllocation Allocation(BudgetPeriod period, BudgetLine line, decimal amount) => new()
    {
        BudgetPeriodId = period.Id,
        BudgetLineId = line.Id,
        Amount = amount
    };

    private static FinancialTransaction Transaction(Budget budget, DateOnly date, decimal amount, TransactionDirection direction) => new()
    {
        BudgetId = budget.Id,
        TransactionDate = date,
        Description = $"{direction} transaction",
        Amount = amount,
        Direction = direction
    };

    private static TransactionAssignment Assignment(FinancialTransaction transaction, BudgetLine line, decimal amount) => new()
    {
        TransactionId = transaction.Id,
        BudgetLineId = line.Id,
        Amount = amount
    };

    private static BudgetReallocation Reallocation(BudgetPeriod period, BudgetLine from, BudgetLine to, decimal amount) => new()
    {
        BudgetPeriodId = period.Id,
        FromBudgetLineId = from.Id,
        ToBudgetLineId = to.Id,
        Amount = amount,
        Reason = "Rebalance period budget"
    };
}

internal static class DashboardTestExtensions
{
    public static BudgetLineSummary Line(this PeriodSummary summary, BudgetLine line) =>
        summary.Lines.Single(snapshot => snapshot.BudgetLineId == line.Id);
}
