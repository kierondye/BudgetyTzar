using FluentValidation;

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

public sealed class RecordReallocationHandler(IEffectiveBudgetRepository effectiveBudgets)
{
    public async Task<CommandResult<BudgetReallocation>> Handle(
        Guid budgetId,
        DateOnly date,
        string? notes,
        IReadOnlyList<BudgetReallocationAdjustmentItem> adjustments,
        CancellationToken ct)
    {
        var budgetResult = await effectiveBudgets.GetEffectiveBudget(budgetId, date, ct);
        if (budgetResult is EffectiveBudgetLoadResult.BudgetNotFound)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        var moneyAdjustments = new List<EffectiveBudgetReallocationAdjustment>(adjustments.Count);
        foreach (var adjustment in adjustments)
        {
            var amountResult = PositiveMoneyAmount.Create(adjustment.Amount);
            if (amountResult is PositiveMoneyAmountResult.ValidationFailed moneyValidationProblem)
            {
                return CommandResult<BudgetReallocation>.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(adjustments)] = [moneyValidationProblem.Error]
                });
            }

            moneyAdjustments.Add(new EffectiveBudgetReallocationAdjustment(
                adjustment.BudgetItemId,
                ((PositiveMoneyAmountResult.Success)amountResult).Amount,
                adjustment.Direction));
        }

        if (budgetResult is not EffectiveBudgetLoadResult.Success loaded)
        {
            throw new InvalidOperationException("Effective budget load result was not handled.");
        }

        var result = loaded.Budget.RecordReallocation(moneyAdjustments, notes);
        if (result is EffectiveBudgetResult.ItemNotFound)
        {
            return CommandResult<BudgetReallocation>.NotFound();
        }

        if (result is EffectiveBudgetResult.ItemArchived)
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        if (result is EffectiveBudgetResult.ValidationFailed effectiveBudgetValidationProblem)
        {
            return CommandResult<BudgetReallocation>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(adjustments)] = [effectiveBudgetValidationProblem.Error]
            });
        }

        if (result is not EffectiveBudgetResult.Success success)
        {
            throw new InvalidOperationException("Effective budget reallocation result was not handled.");
        }

        var createdReallocation = success.Budget.PendingReallocations.Single();

        var saved = await effectiveBudgets.Save(success.Budget, ct);
        var projectionEventId = saved.EventIds.Count > 0 ? saved.EventIds[0] : (Guid?)null;
        return CommandResult<BudgetReallocation>.Created(createdReallocation, projectionEventId);
    }
}
