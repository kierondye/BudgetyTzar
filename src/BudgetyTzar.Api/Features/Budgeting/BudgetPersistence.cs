using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Features.Budgeting;

public interface IBudgetRepository
{
    BudgetSaveResult Save(Budget budget);

    BudgetSaveResult Save(EntityState<Budget> budgetState);

    bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null);

    IReadOnlyList<Budget> GetAll();

    EntityState<Budget>? Get(Guid budgetId);

    BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId);

    BudgetItemReference? GetBudgetItemReference(Guid budgetItemId);
}
