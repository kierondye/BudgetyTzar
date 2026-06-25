using BudgetyTzar.Api.Application.Budgeting;
using BudgetyTzar.Api.Application.Common;
using BudgetyTzar.Api.Contracts.Events;
using BudgetyTzar.Api.Infrastructure.Events;
using BudgetyTzar.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Features;

public sealed record CreateBudgetItemReallocationRequest(DateOnly Date, string? Notes, IReadOnlyList<BudgetReallocationAdjustmentItem> Adjustments);

public sealed record BudgetReallocationDto(
    Guid Id,
    Guid BudgetId,
    DateOnly Date,
    string? Notes,
    IReadOnlyList<BudgetReallocationAdjustmentItem> Adjustments,
    DateTimeOffset CreatedAt);

public sealed class CreateBudgetItemReallocationValidator : AbstractValidator<CreateBudgetItemReallocationRequest>
{
    public CreateBudgetItemReallocationValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Adjustments).NotNull().Must(x => x.Count >= 2)
            .WithMessage("A reallocation must contain at least two adjustments.");
        RuleForEach(x => x.Adjustments).ChildRules(item =>
        {
            item.RuleFor(x => x.BudgetItemId).NotEmpty();
            item.RuleFor(x => x.Amount).PositiveAmount();
            item.RuleFor(x => x.Direction).IsInEnum();
        });
    }
}

public sealed class RecordReallocationHandler(
    BudgetDbContext db,
    DomainEventOutboxWriter events,
    BudgetItemEligibilityService eligibility)
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
