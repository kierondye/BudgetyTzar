using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Budgeting;

public sealed class RecordAdjustmentHandler(BudgetDbContext db, DomainEventOutboxWriter events, BudgetItemEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetAdjustment>> HandleCanonical(
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var item = await eligibility.GetBudgetItem(budgetId, budgetItemId, ct);
        if (item is null)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        if (!item.CanAcceptActivityOn(date))
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        var adjustment = BudgetAdjustment.Create(budgetId, budgetItemId, amount, type, date, notes);
        if (!await NetPlannedSpendingIsValid(budgetId, adjustment, ct))
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Net planned spending must not exceed net planned income."]
            });
        }

        db.BudgetAdjustments.Add(adjustment);
        var eventId = events.Add(adjustment.RecordedEvent(budgetId, item.Name));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetAdjustment>.Created(adjustment, eventId);
    }

    private async Task<bool> NetPlannedSpendingIsValid(Guid budgetId, BudgetAdjustment pending, CancellationToken ct)
    {
        var existing = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date <= pending.Date)
            .ToListAsync(ct);
        var net = existing.Sum(SignedPlannedAmount) + SignedPlannedAmount(pending);
        return net >= 0;
    }

    private static decimal SignedPlannedAmount(BudgetAdjustment adjustment) =>
        adjustment.Type == BudgetAdjustmentType.Credit ? adjustment.Amount : -adjustment.Amount;
}

public sealed class RecordReallocationHandler(BudgetDbContext db, DomainEventOutboxWriter events, BudgetItemEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetReallocation>> HandleCanonical(
        Guid budgetId,
        DateOnly date,
        string? notes,
        IReadOnlyList<BudgetReallocationAdjustmentItem> adjustments,
        CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        if (adjustments.Count < 2)
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(adjustments)] = ["A reallocation must contain at least two adjustments."]
            });
        }

        var creditTotal = adjustments.Where(x => x.Direction == BudgetAdjustmentType.Credit).Sum(x => x.Amount);
        var debitTotal = adjustments.Where(x => x.Direction == BudgetAdjustmentType.Debit).Sum(x => x.Amount);
        if (creditTotal != debitTotal)
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(adjustments)] = ["Reallocation credits must equal reallocation debits."]
            });
        }

        var itemIds = adjustments.Select(x => x.BudgetItemId).Distinct().ToArray();
        var items = await eligibility.GetBudgetItems(budgetId, itemIds, ct);
        if (items.Count != itemIds.Length)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        if (items.Any(x => !x.CanAcceptActivityOn(date)))
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        var reallocation = BudgetReallocation.Create(budgetId, date, notes);

        db.BudgetReallocations.Add(reallocation);
        db.BudgetAdjustments.AddRange(adjustments.Select(x =>
            BudgetAdjustment.Create(budgetId, x.BudgetItemId, x.Amount, x.Direction, date, notes, reallocation.Id)));
        var eventId = events.Add(reallocation.RecordedEvent(
            budgetId,
            adjustments
                .Select(x => new BudgetReallocationAdjustmentPayload(x.BudgetItemId, x.Amount, x.Direction))
                .ToList()));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetReallocation>.Created(reallocation, eventId);
    }
}

internal static class BudgetItemValidationErrors
{
    public static Dictionary<string, string[]> ArchivedBudgetItemErrors() => new()
    {
        ["budgetItemId"] = ["Archived budget items can only be used for retrospective corrections dated on or before the archive date."]
    };
}
