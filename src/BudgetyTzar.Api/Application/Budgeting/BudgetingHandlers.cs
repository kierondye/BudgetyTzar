using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Application.Reporting;
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

public sealed class CreateBudgetPeriodHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetPeriod>> Handle(
        Guid budgetId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<BudgetLineAllocationItem>? inlineAllocations,
        Guid? copyAllocationsFromPeriodId,
        CancellationToken ct)
    {
        if (!await db.Budgets.AnyAsync(x => x.Id == budgetId, ct))
        {
            return CommandResult<BudgetPeriod>.NotFound();
        }

        var requestedRange = new DateRange(startDate, endDate);
        var overlaps = await db.BudgetPeriods.AnyAsync(x =>
            x.BudgetId == budgetId
            && x.StartDate <= requestedRange.EndDate
            && requestedRange.StartDate <= x.EndDate,
            ct);
        if (overlaps)
        {
            return CommandResult<BudgetPeriod>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(startDate)] = ["Budget periods cannot overlap within the same budget."]
            });
        }

        IReadOnlyList<BudgetLineAllocationItem> allocations = [];
        if (inlineAllocations is not null)
        {
            if (await ValidateAllocations(budgetId, null, inlineAllocations, nameof(inlineAllocations), ct) is { } allocationProblem)
            {
                if (allocationProblem.ContainsKey("__notFound"))
                {
                    return CommandResult<BudgetPeriod>.NotFound();
                }

                return CommandResult<BudgetPeriod>.ValidationProblem(allocationProblem);
            }

            allocations = inlineAllocations;
        }
        else if (copyAllocationsFromPeriodId.HasValue)
        {
            var sourcePeriodId = copyAllocationsFromPeriodId.Value;
            if (!await db.BudgetPeriods.AnyAsync(x => x.BudgetId == budgetId && x.Id == sourcePeriodId, ct))
            {
                return CommandResult<BudgetPeriod>.NotFound();
            }

            allocations = await db.BudgetLineAllocations
                .AsNoTracking()
                .Join(
                    db.BudgetLines.AsNoTracking().Where(x => x.BudgetId == budgetId && !x.IsArchived),
                    allocation => allocation.BudgetLineId,
                    line => line.Id,
                    (allocation, _) => allocation)
                .Where(x => x.BudgetPeriodId == sourcePeriodId)
                .OrderBy(x => x.Id)
                .Select(x => new BudgetLineAllocationItem(x.BudgetLineId, x.Amount))
                .ToListAsync(ct);
        }

        var period = BudgetPeriod.Create(budgetId, name, startDate, endDate);
        db.BudgetPeriods.Add(period);
        db.BudgetLineAllocations.AddRange(period.ReplaceAllocations(allocations));
        audit.Add(period.CreatedEvent());
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetPeriod>.Created(period);
    }

    private async Task<IReadOnlyDictionary<string, string[]>?> ValidateAllocations(
        Guid budgetId,
        Guid? periodId,
        IReadOnlyList<BudgetLineAllocationItem> allocations,
        string requestPropertyName,
        CancellationToken ct)
    {
        var lineIds = allocations.Select(x => x.BudgetLineId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
        {
            return new Dictionary<string, string[]>
            {
                [requestPropertyName] = ["A budget line can only be allocated once per period."]
            };
        }

        var lines = await eligibility.GetEligibleBudgetLines(budgetId, periodId, lineIds, ct);
        return lines.Count == lineIds.Length
            ? null
            : new Dictionary<string, string[]> { ["__notFound"] = ["One or more budget lines were not found."] };
    }
}

public sealed class ReplaceAllocationsHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult> Handle(Guid budgetId, Guid periodId, IReadOnlyList<BudgetLineAllocationItem> allocations, CancellationToken ct)
    {
        if (!await db.BudgetPeriods.AnyAsync(x => x.BudgetId == budgetId && x.Id == periodId, ct))
        {
            return CommandResult.NotFound();
        }

        var lineIds = allocations.Select(x => x.BudgetLineId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
        {
            return CommandResult.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(allocations)] = ["A budget line can only be allocated once per period."]
            });
        }

        var lines = await eligibility.GetEligibleBudgetLines(budgetId, periodId, lineIds, ct);
        if (lines.Count != lineIds.Length)
        {
            return CommandResult.NotFound();
        }

        var period = await db.BudgetPeriods.AsNoTracking().FirstAsync(x => x.Id == periodId, ct);
        var existing = await db.BudgetLineAllocations
            .Where(x => x.BudgetPeriodId == periodId)
            .ToListAsync(ct);
        db.BudgetLineAllocations.RemoveRange(existing);
        db.BudgetLineAllocations.AddRange(period.ReplaceAllocations(allocations));
        audit.Add(new DomainEvent(
            "BudgetLineAllocationsReplaced",
            budgetId,
            periodId,
            nameof(BudgetLineAllocation),
            periodId,
            $"Replaced {allocations.Count} allocation(s).",
            $"Previous={existing.Count}; New={allocations.Count}"));
        await db.SaveChangesAsync(ct);
        return CommandResult.NoContent();
    }
}

public sealed class RecordAdjustmentHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetAdjustment>> Handle(Guid budgetId, Guid periodId, Guid budgetLineId, decimal amount, string reason, CancellationToken ct)
    {
        if (!await db.BudgetPeriods.AnyAsync(x => x.BudgetId == budgetId && x.Id == periodId, ct))
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var line = await eligibility.GetEligibleBudgetLine(budgetId, periodId, budgetLineId, ct);
        if (line is null)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var period = await db.BudgetPeriods.AsNoTracking().FirstAsync(x => x.Id == periodId, ct);
        var adjustment = period.RecordAdjustment(budgetLineId, amount, reason);
        db.BudgetAdjustments.Add(adjustment);
        audit.Add(adjustment.RecordedEvent(budgetId, line.Name));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetAdjustment>.Created(adjustment);
    }
}

public sealed class RecordReallocationHandler(BudgetDbContext db, AuditEventWriter audit, BudgetLineEligibilityService eligibility)
{
    public async Task<CommandResult<BudgetReallocation>> Handle(Guid budgetId, Guid periodId, Guid fromBudgetLineId, Guid toBudgetLineId, decimal amount, string reason, CancellationToken ct)
    {
        if (!await db.BudgetPeriods.AnyAsync(x => x.BudgetId == budgetId && x.Id == periodId, ct))
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        var lineIds = new[] { fromBudgetLineId, toBudgetLineId };
        var budgetLines = await eligibility.GetEligibleBudgetLines(budgetId, periodId, lineIds, ct);
        if (budgetLines.Count != lineIds.Length)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        var nonDebitLineIds = budgetLines
            .Where(x => x.Direction != BudgetLineDirection.Debit)
            .Select(x => x.Id)
            .ToArray();
        if (nonDebitLineIds.Length > 0)
        {
            var errors = new Dictionary<string, string[]>
            {
                ["request"] = ["Budget reallocations can only be created between debit budget lines."]
            };
            if (nonDebitLineIds.Contains(fromBudgetLineId))
            {
                errors[nameof(fromBudgetLineId)] = ["Source budget line must be a debit line."];
            }

            if (nonDebitLineIds.Contains(toBudgetLineId))
            {
                errors[nameof(toBudgetLineId)] = ["Target budget line must be a debit line."];
            }

            return CommandResult<BudgetReallocation>.ValidationProblem(errors);
        }

        var summary = await DashboardQueries.GetPeriodSummaryFromOperationalTables(db, budgetId, periodId, ct);
        var sourceLine = summary?.Lines.FirstOrDefault(x => x.BudgetLineId == fromBudgetLineId);
        if (sourceLine is null)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        if (sourceLine.ClosingBalance < amount)
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = ["Reallocation amount cannot exceed the source budget line's available balance."]
            });
        }

        var period = await db.BudgetPeriods.AsNoTracking().FirstAsync(x => x.Id == periodId, ct);
        var reallocation = period.RecordReallocation(fromBudgetLineId, toBudgetLineId, amount, reason);
        db.BudgetReallocations.Add(reallocation);
        audit.Add(reallocation.RecordedEvent(budgetId));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetReallocation>.Created(reallocation);
    }
}
