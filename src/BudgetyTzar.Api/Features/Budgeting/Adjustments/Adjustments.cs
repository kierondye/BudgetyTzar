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
    DomainEventOutboxWriter events)
{
    public async Task<CommandResult<BudgetAdjustment>> Handle(
        Guid budgetId,
        Guid budgetItemId,
        decimal amount,
        BudgetAdjustmentType type,
        DateOnly date,
        string? notes,
        CancellationToken ct)
    {
        var budgetExists = await BudgetExists(budgetId, ct);
        if (!budgetExists)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var item = await LoadBudgetItem(budgetId, budgetItemId, ct);
        var effectivePlannedAmounts = await LoadEffectivePlannedAmounts(budgetId, date, ct);
        var effectiveBudget = HydrateEffectiveBudget(budgetId, budgetItemId, date, item, effectivePlannedAmounts);

        var result = effectiveBudget.RecordAdjustment(budgetItemId, amount, type, notes);
        if (result is EffectiveBudgetResult.ItemNotFound)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        if (result is EffectiveBudgetResult.ItemArchived)
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        if (result is EffectiveBudgetResult.ValidationFailed validationProblem)
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = [validationProblem.Error]
            });
        }

        if (result is not EffectiveBudgetResult.Success success)
        {
            throw new InvalidOperationException("Effective budget adjustment result was not handled.");
        }

        var adjustment = success.Budget.PendingAdjustments.Single();
        var domainEvent = success.Budget.PendingEvents.Single();
        db.BudgetAdjustments.Add(adjustment);
        var eventId = events.Add(domainEvent);
        await db.SaveChangesAsync(ct);
        return CommandResult<BudgetAdjustment>.Created(adjustment, eventId);
    }

    private Task<bool> BudgetExists(Guid budgetId, CancellationToken ct) =>
        db.Budgets
            .AsNoTracking()
            .AnyAsync(x => x.Id == budgetId, ct);

    private Task<BudgetItem?> LoadBudgetItem(Guid budgetId, Guid budgetItemId, CancellationToken ct) =>
        db.BudgetItems
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.BudgetId == budgetId && x.Id == budgetItemId, ct);

    private Task<List<EffectivePlannedAmount>> LoadEffectivePlannedAmounts(
        Guid budgetId,
        DateOnly date,
        CancellationToken ct) =>
        db.BudgetAdjustments
            .AsNoTracking()
            .Where(x => x.BudgetId == budgetId && x.Date <= date)
            .GroupBy(x => x.BudgetItemId)
            .Select(x => new EffectivePlannedAmount(
                x.Key,
                x.Sum(y => y.Type == BudgetAdjustmentType.Credit ? y.Amount : -y.Amount)))
            .ToListAsync(ct);

    private static EffectiveBudget HydrateEffectiveBudget(
        Guid budgetId,
        Guid budgetItemId,
        DateOnly date,
        BudgetItem? item,
        IReadOnlyCollection<EffectivePlannedAmount> effectivePlannedAmounts)
    {
        var itemPlannedAmount = effectivePlannedAmounts
            .SingleOrDefault(x => x.BudgetItemId == budgetItemId)
            ?.PlannedAmount ?? 0m;
        IReadOnlyCollection<EffectiveBudgetItemState> effectiveBudgetItems = item is null
            ? []
            : [new EffectiveBudgetItemState(item, itemPlannedAmount)];

        return new EffectiveBudget(
            budgetId,
            date,
            effectivePlannedAmounts.Sum(x => x.PlannedAmount),
            effectiveBudgetItems);
    }
}

internal sealed record EffectivePlannedAmount(Guid BudgetItemId, decimal PlannedAmount);
