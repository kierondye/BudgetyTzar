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
        var budget = await db.Budgets.SingleOrDefaultAsync(x => x.Id == budgetId, ct);
        if (budget is null)
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
        var existingAdjustments = await db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date <= adjustment.Date)
            .ToListAsync(ct);
        if (!budget.CanRecordAdjustment(existingAdjustments, adjustment))
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = [Budget.NetPlannedSpendingExceededMessage]
            });
        }

        db.BudgetAdjustments.Add(adjustment);
        var eventId = events.Add(adjustment.RecordedEvent(item.Name));
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetAdjustment>.Created(adjustment, eventId);
    }
}
