using System.Collections.Immutable;
using BudgetyTzar.Api.Domain.ValueTypes;

namespace BudgetyTzar.Api.Domain.Entities;

public sealed class Budget
{
    internal Budget(
        Guid budgetId,
        NormalizedName name,
        CurrencyCode currency,
        ImmutableArray<BudgetItem> budgetItems)
        : this(budgetId, name, currency, budgetItems, [])
    {
    }

    private Budget(
        Guid budgetId,
        NormalizedName name,
        CurrencyCode currency,
        ImmutableArray<BudgetItem> budgetItems,
        ImmutableArray<AuditFact> auditFacts)
    {
        BudgetId = budgetId;
        Name = name;
        Currency = currency;
        BudgetItems = budgetItems;
        AuditFacts = auditFacts;
    }

    public Guid BudgetId { get; }

    public NormalizedName Name { get; }

    public CurrencyCode Currency { get; }

    public ImmutableArray<BudgetItem> BudgetItems { get; }

    public ImmutableArray<AuditFact> AuditFacts { get; }

    public static CreateBudgetResult Create(Guid budgetId, NormalizedName name, CurrencyCode currency)
    {
        if (budgetId == Guid.Empty)
        {
            return new CreateBudgetResult.InvalidIdentity();
        }

        return new CreateBudgetResult.Created(new Budget(budgetId, name, currency, []));
    }

    internal static CreateBudgetResult CreateForCommand(
        Guid budgetId,
        NormalizedName name,
        CurrencyCode currency)
    {
        return Create(budgetId, name, currency) switch
        {
            CreateBudgetResult.Created created => new CreateBudgetResult.Created(
                created.Budget.WithAuditFact(AuditAction.BudgetCreated, null, created.Budget)),
            CreateBudgetResult.InvalidIdentity invalid => invalid,
            _ => throw new InvalidOperationException("Unexpected create budget result.")
        };
    }

    public RenameBudgetResult Rename(NormalizedName name)
    {
        var budget = new Budget(BudgetId, name, Currency, BudgetItems, AuditFacts);
        return new RenameBudgetResult.Renamed(WithAuditFact(AuditAction.BudgetRenamed, this, budget));
    }

    public AddBudgetItemResult AddBudgetItem(
        Guid budgetItemId,
        NormalizedName name,
        BudgetItemKind kind,
        PositiveMoneyAmount plannedAmount)
    {
        if (HasBudgetItemNamed(name))
        {
            return new AddBudgetItemResult.DuplicateName();
        }

        if (BudgetItem.Create(budgetItemId, name, kind, plannedAmount) is not CreateBudgetItemEntityResult.Created created)
        {
            return new AddBudgetItemResult.InvalidIdentity();
        }

        var budgetItem = created.BudgetItem;
        var budget = new Budget(BudgetId, Name, Currency, BudgetItems.Add(budgetItem), AuditFacts);

        return new AddBudgetItemResult.Added(
            budget.WithAuditFact(AuditAction.BudgetItemCreated, this, budget),
            budgetItem);
    }

    public RenameBudgetItemResult RenameBudgetItem(Guid budgetItemId, NormalizedName name)
    {
        if (HasBudgetItemNamed(name, budgetItemId))
        {
            return new RenameBudgetItemResult.DuplicateName();
        }

        if (GetBudgetItem(budgetItemId) is not GetBudgetItemResult.Found found)
        {
            return new RenameBudgetItemResult.NotFound();
        }

        var renamedBudgetItem = found.BudgetItem.Rename(name);
        var budget = ReplaceBudgetItem(budgetItemId, renamedBudgetItem);

        return new RenameBudgetItemResult.Renamed(
            WithAuditFact(AuditAction.BudgetItemRenamed, this, budget),
            renamedBudgetItem);
    }

    public ChangeBudgetItemPlannedAmountResult ChangeBudgetItemPlannedAmount(Guid budgetItemId, PositiveMoneyAmount plannedAmount)
    {
        if (GetBudgetItem(budgetItemId) is not GetBudgetItemResult.Found found)
        {
            return new ChangeBudgetItemPlannedAmountResult.NotFound();
        }

        var updatedBudgetItem = found.BudgetItem.ChangePlannedAmount(plannedAmount);
        var budget = ReplaceBudgetItem(budgetItemId, updatedBudgetItem);

        return new ChangeBudgetItemPlannedAmountResult.Changed(
            WithAuditFact(AuditAction.BudgetItemPlannedAmountChanged, this, budget),
            updatedBudgetItem);
    }

    public RemoveBudgetItemResult RemoveBudgetItem(Guid budgetItemId)
    {
        if (GetBudgetItem(budgetItemId) is not GetBudgetItemResult.Found)
        {
            return new RemoveBudgetItemResult.NotFound();
        }

        var budgetItems = BudgetItems.RemoveAll(budgetItem => budgetItem.BudgetItemId == budgetItemId);
        var budget = new Budget(BudgetId, Name, Currency, budgetItems, AuditFacts);
        return new RemoveBudgetItemResult.Removed(WithAuditFact(AuditAction.BudgetItemDeleted, this, budget));
    }

    public GetBudgetItemResult GetBudgetItem(Guid budgetItemId)
    {
        foreach (var budgetItem in BudgetItems)
        {
            if (budgetItem.BudgetItemId == budgetItemId)
            {
                return new GetBudgetItemResult.Found(budgetItem);
            }
        }

        return new GetBudgetItemResult.NotFound();
    }

    public bool HasBudgetItemNamed(NormalizedName name, Guid? exceptBudgetItemId = null)
    {
        return BudgetItems.Any(budgetItem =>
            budgetItem.BudgetItemId != exceptBudgetItemId
            && budgetItem.Name == name);
    }

    private Budget ReplaceBudgetItem(Guid budgetItemId, BudgetItem budgetItem)
    {
        var budgetItems = BudgetItems
            .Select(existingBudgetItem => existingBudgetItem.BudgetItemId == budgetItemId
                ? budgetItem
                : existingBudgetItem)
            .ToImmutableArray();

        return new Budget(BudgetId, Name, Currency, budgetItems, AuditFacts);
    }

    private Budget WithAuditFact(AuditAction action, Budget? oldValue, Budget? newValue)
    {
        var target = newValue ?? this;

        return new Budget(
            target.BudgetId,
            target.Name,
            target.Currency,
            target.BudgetItems,
            target.AuditFacts.Add(AuditFact.Create(
                action,
                oldValue is null ? null : AuditValueSerializer.Serialize(oldValue),
                newValue is null ? null : AuditValueSerializer.Serialize(newValue))));
    }
}

public abstract record CreateBudgetResult
{
    public sealed record Created(Budget Budget) : CreateBudgetResult;

    public sealed record InvalidIdentity : CreateBudgetResult;
}

public abstract record RenameBudgetResult
{
    public sealed record Renamed(Budget Budget) : RenameBudgetResult;
}

public abstract record AddBudgetItemResult
{
    public sealed record Added(Budget Budget, BudgetItem BudgetItem) : AddBudgetItemResult;

    public sealed record DuplicateName : AddBudgetItemResult;

    public sealed record InvalidIdentity : AddBudgetItemResult;
}

public abstract record RenameBudgetItemResult
{
    public sealed record Renamed(Budget Budget, BudgetItem BudgetItem) : RenameBudgetItemResult;

    public sealed record NotFound : RenameBudgetItemResult;

    public sealed record DuplicateName : RenameBudgetItemResult;
}

public abstract record ChangeBudgetItemPlannedAmountResult
{
    public sealed record Changed(Budget Budget, BudgetItem BudgetItem) : ChangeBudgetItemPlannedAmountResult;

    public sealed record NotFound : ChangeBudgetItemPlannedAmountResult;
}

public abstract record RemoveBudgetItemResult
{
    public sealed record Removed(Budget Budget) : RemoveBudgetItemResult;

    public sealed record NotFound : RemoveBudgetItemResult;
}

public abstract record GetBudgetItemResult
{
    public sealed record Found(BudgetItem BudgetItem) : GetBudgetItemResult;

    public sealed record NotFound : GetBudgetItemResult;
}
