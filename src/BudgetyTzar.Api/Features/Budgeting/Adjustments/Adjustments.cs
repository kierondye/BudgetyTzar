using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemAdjustmentRequest(decimal Amount, BudgetAdjustmentType Direction, DateOnly Date, string? Notes);

public sealed record BudgetAdjustmentDto(
    Guid Id,
    Guid BudgetId,
    Guid BudgetItemId,
    Guid? ReallocationId,
    DateOnly Date,
    decimal Amount,
    BudgetAdjustmentType Direction,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed class CreateBudgetItemAdjustmentValidator : AbstractValidator<CreateBudgetItemAdjustmentRequest>
{
    public CreateBudgetItemAdjustmentValidator()
    {
        RuleFor(x => x.Amount).PositiveAmount();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class RecordAdjustmentHandler(
    BudgetDbContext db,
    DomainEventOutboxWriter events,
    BudgetItemEligibilityService eligibility)
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
