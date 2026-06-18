using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Application.Budgeting;

public sealed class CreateBudgetHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<Budget>> Handle(string name, string currency, CancellationToken ct)
    {
        var budget = Budget.Create(name, currency);
        db.Budgets.Add(budget);
        audit.Add(budget.CreatedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult<Budget>.Created(budget);
    }
}

public sealed class CreateBudgetLineHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult<BudgetLine>> Handle(Guid budgetId, string name, BudgetLineDirection direction, BudgetLineRolloverType rolloverType, CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<BudgetLine>.NotFound();
        }

        var trimmedName = name.Trim();
        if (await db.BudgetLines.AnyAsync(x => x.BudgetId == budgetId && x.Name == trimmedName, ct))
        {
            return CommandResult<BudgetLine>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(name)] = ["A budget line with this name already exists in this budget."]
            });
        }

        var line = BudgetLine.Create(budgetId, trimmedName, direction, rolloverType);
        db.BudgetLines.Add(line);
        audit.Add(line.CreatedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetLine>.Created(line);
    }
}

public sealed class ArchiveBudgetLineHandler(BudgetDbContext db, AuditEventWriter audit)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid lineId, CancellationToken ct)
    {
        var line = await db.BudgetLines.FirstOrDefaultAsync(x => x.Id == lineId && x.BudgetId == budgetId, ct);
        if (line is null)
        {
            return CommandResult.NotFound();
        }

        audit.Add(line.Archive());
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class RecordAdjustmentHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetAdjustment>> HandleCanonical(
        Guid budgetId,
        Guid budgetLineId,
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

        var periodId = await BudgetPeriodLookup.FindPeriodIdForDate(db, budgetId, date, ct);
        var line = await eligibility.GetEligibleBudgetLine(budgetId, periodId, budgetLineId, ct);
        if (line is null)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var adjustment = BudgetAdjustment.Create(budgetId, budgetLineId, amount, type, date, notes, periodId);
        if (!await NetPlannedSpendingIsValid(budgetId, adjustment, ct))
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Net planned spending must not exceed net planned income."]
            });
        }

        db.BudgetAdjustments.Add(adjustment);
        audit.Add(adjustment.RecordedEvent(budgetId, line.Name));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetAdjustment>.Created(adjustment);
    }

    private async Task<bool> NetPlannedSpendingIsValid(Guid budgetId, BudgetAdjustment pending, CancellationToken ct)
    {
        var existing = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId || x.BudgetId == Guid.Empty)
            .ToListAsync(ct);
        var net = existing.Sum(SignedPlannedAmount) + SignedPlannedAmount(pending);
        return net >= 0;
    }

    private static decimal SignedPlannedAmount(BudgetAdjustment adjustment) =>
        adjustment.Type == BudgetAdjustmentType.Credit ? adjustment.Amount : -adjustment.Amount;
}

public sealed class RecordReallocationHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
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

        var periodId = await BudgetPeriodLookup.FindPeriodIdForDate(db, budgetId, date, ct);
        var lineIds = adjustments.Select(x => x.BudgetItemId).Distinct().ToArray();
        var lines = await eligibility.GetEligibleBudgetLines(budgetId, periodId, lineIds, ct);
        if (lines.Count != lineIds.Length)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        var reallocation = BudgetReallocation.Create(budgetId, date, notes);
        if (periodId.HasValue)
        {
            reallocation.BudgetPeriodId = periodId.Value;
        }

        db.BudgetReallocations.Add(reallocation);
        db.BudgetAdjustments.AddRange(adjustments.Select(x =>
            BudgetAdjustment.Create(budgetId, x.BudgetItemId, x.Amount, x.Direction, date, notes, periodId, reallocation.Id)));
        audit.Add(reallocation.RecordedEvent(budgetId));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetReallocation>.Created(reallocation);
    }
}
