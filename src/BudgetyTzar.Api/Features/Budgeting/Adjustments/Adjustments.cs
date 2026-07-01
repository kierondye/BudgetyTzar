using FluentValidation;

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

public sealed class RecordAdjustmentHandler(IEffectiveBudgetRepository effectiveBudgets)
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
        var budgetResult = await effectiveBudgets.GetEffectiveBudget(budgetId, date, ct);
        if (budgetResult is EffectiveBudgetLoadResult.BudgetNotFound)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        var amountResult = PositiveMoneyAmount.Create(amount);
        if (amountResult is PositiveMoneyAmountResult.ValidationFailed moneyValidationProblem)
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = [moneyValidationProblem.Error]
            });
        }

        if (budgetResult is not EffectiveBudgetLoadResult.Success loaded)
        {
            throw new InvalidOperationException("Effective budget load result was not handled.");
        }

        var result = loaded.Budget.RecordAdjustment(
            budgetItemId,
            ((PositiveMoneyAmountResult.Success)amountResult).Amount,
            type,
            notes);
        if (result is EffectiveBudgetResult.ItemNotFound)
        {
            return CommandResult<BudgetAdjustment>.NotFound();
        }

        if (result is EffectiveBudgetResult.ItemArchived)
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(BudgetItemValidationErrors.ArchivedBudgetItemErrors());
        }

        if (result is EffectiveBudgetResult.ValidationFailed effectiveBudgetValidationProblem)
        {
            return CommandResult<BudgetAdjustment>.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(amount)] = [effectiveBudgetValidationProblem.Error]
            });
        }

        if (result is not EffectiveBudgetResult.Success success)
        {
            throw new InvalidOperationException("Effective budget adjustment result was not handled.");
        }

        var createdAdjustment = success.Budget.PendingAdjustments.Single();

        var saved = await effectiveBudgets.Save(success.Budget, ct);
        var projectionEventId = saved.EventIds.Count > 0 ? saved.EventIds[0] : (Guid?)null;
        return CommandResult<BudgetAdjustment>.Created(createdAdjustment, projectionEventId);
    }
}
