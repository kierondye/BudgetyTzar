using System.Globalization;
using BudgetyTzar.Api.Features.Budgeting;

namespace BudgetyTzar.Api.Features.Reporting;

public sealed record BudgetSummaryResponse(
    Guid BudgetId,
    string Name,
    string Currency,
    BudgetSummarySectionResponse Funding,
    BudgetSummarySectionResponse Consumption,
    BudgetSummaryOverallResponse Overall)
{
    public static BudgetSummaryResponse FromSummary(BudgetSummary summary)
    {
        return new BudgetSummaryResponse(
            summary.BudgetId,
            summary.Name,
            summary.Currency,
            BudgetSummarySectionResponse.FromSection(summary.Funding),
            BudgetSummarySectionResponse.FromSection(summary.Consumption),
            BudgetSummaryOverallResponse.FromOverall(summary.Overall));
    }
}

public sealed record BudgetSummarySectionResponse(
    IReadOnlyList<BudgetSummaryItemResponse> Items,
    string TotalPlannedAmount,
    string TotalActualAmount,
    string TotalRemainingAmount)
{
    public static BudgetSummarySectionResponse FromSection(BudgetSummarySection section)
    {
        return new BudgetSummarySectionResponse(
            section.Items.Select(BudgetSummaryItemResponse.FromItem).ToList(),
            Format(section.TotalPlannedAmount),
            Format(section.TotalActualAmount),
            Format(section.TotalRemainingAmount));
    }

    private static string Format(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

public sealed record BudgetSummaryItemResponse(
    Guid BudgetItemId,
    string Name,
    string PlannedAmount,
    string ActualAmount,
    string RemainingAmount)
{
    public static BudgetSummaryItemResponse FromItem(BudgetSummaryItem item)
    {
        return new BudgetSummaryItemResponse(
            item.BudgetItemId,
            item.Name,
            Format(item.PlannedAmount),
            Format(item.ActualAmount),
            Format(item.RemainingAmount));
    }

    private static string Format(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

public sealed record BudgetSummaryOverallResponse(string PlannedSurplus, string ActualSurplus)
{
    public static BudgetSummaryOverallResponse FromOverall(BudgetSummaryOverall overall)
    {
        return new BudgetSummaryOverallResponse(
            Format(overall.PlannedSurplus),
            Format(overall.ActualSurplus));
    }

    private static string Format(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

public sealed record BudgetSummary(
    Guid BudgetId,
    string Name,
    string Currency,
    BudgetSummarySection Funding,
    BudgetSummarySection Consumption,
    BudgetSummaryOverall Overall);

public sealed record BudgetSummarySection(
    IReadOnlyList<BudgetSummaryItem> Items,
    decimal TotalPlannedAmount,
    decimal TotalActualAmount,
    decimal TotalRemainingAmount);

public sealed record BudgetSummaryItem(
    Guid BudgetItemId,
    string Name,
    decimal PlannedAmount,
    decimal ActualAmount,
    decimal RemainingAmount);

public sealed record BudgetSummaryOverall(decimal PlannedSurplus, decimal ActualSurplus);

public abstract record GetBudgetSummaryResult
{
    public sealed record Found(BudgetSummary Summary) : GetBudgetSummaryResult;

    public sealed record NotFound : GetBudgetSummaryResult;
}
