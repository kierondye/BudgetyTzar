using System.Globalization;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class PostgreSqlBudgetRepository : IBudgetRepository
{
    private const string BudgetPrimaryKeyConstraint = "pk_budgets";
    private const string BudgetNameConstraint = "ux_budgets_application_user_id_name";
    private const string AllocationBudgetItemConstraint = "fk_allocations_budget_item_owner_currency";

    private readonly BudgetyTzarDbContext context;
    private readonly ApplicationUserId userId;

    public PostgreSqlBudgetRepository(BudgetyTzarDbContext context, ICurrentUser currentUser)
    {
        this.context = context;
        userId = currentUser.UserId;
    }

    public BudgetSaveResult Save(Budget budget)
    {
        var applicationUserId = userId.Value;

        if (context.Budgets.AsNoTracking().Any(existingBudget => existingBudget.BudgetId == budget.BudgetId))
        {
            return new BudgetSaveResult.DuplicateIdentity();
        }

        if (HasBudgetNamed(budget.Name))
        {
            return new BudgetSaveResult.DuplicateName();
        }

        context.Budgets.Add(ToRecord(budget, applicationUserId));
        context.BudgetItems.AddRange(ToItemRecords(budget, applicationUserId));

        try
        {
            context.SaveChanges();
            ClearChanges();
            return new BudgetSaveResult.Saved(budget);
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, BudgetPrimaryKeyConstraint))
        {
            ClearChanges();
            return new BudgetSaveResult.DuplicateIdentity();
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, BudgetNameConstraint))
        {
            ClearChanges();
            return new BudgetSaveResult.DuplicateName();
        }
    }

    public BudgetSaveResult Save(EntityState<Budget> budgetState)
    {
        if (budgetState is not PostgreSqlEntityState<Budget> postgreSqlState)
        {
            return new BudgetSaveResult.InvalidState();
        }

        var budget = postgreSqlState.Value;
        var applicationUserId = userId.Value;
        using var transaction = context.Database.BeginTransaction();

        var existingBudget = context.Budgets
            .AsNoTracking()
            .SingleOrDefault(record => record.BudgetId == budget.BudgetId);

        if (existingBudget is null || existingBudget.ApplicationUserId != applicationUserId)
        {
            return new BudgetSaveResult.NotFound();
        }

        if (existingBudget.Version != postgreSqlState.Version)
        {
            return new BudgetSaveResult.StaleState();
        }

        if (HasBudgetNamed(budget.Name, budget.BudgetId))
        {
            return new BudgetSaveResult.DuplicateName();
        }

        var existingItems = context.BudgetItems
            .Where(item => item.BudgetId == budget.BudgetId && item.ApplicationUserId == applicationUserId)
            .ToList();
        var updatedItemIds = budget.BudgetItems
            .Select(item => item.BudgetItemId)
            .ToHashSet();
        var removedItemIds = existingItems
            .Where(item => !updatedItemIds.Contains(item.BudgetItemId))
            .Select(item => item.BudgetItemId)
            .ToHashSet();

        if (removedItemIds.Count > 0
            && context.TransactionAllocations.Any(allocation =>
                allocation.ApplicationUserId == applicationUserId
                && removedItemIds.Contains(allocation.BudgetItemId)))
        {
            return new BudgetSaveResult.BudgetItemHasAllocations();
        }

        try
        {
            var updatedRows = context.Budgets
                .Where(record =>
                    record.BudgetId == budget.BudgetId
                    && record.ApplicationUserId == applicationUserId
                    && record.Version == postgreSqlState.Version)
                .ExecuteUpdate(setters => setters
                    .SetProperty(record => record.Name, budget.Name.Value)
                    .SetProperty(record => record.Currency, budget.Currency.Value)
                    .SetProperty(record => record.Version, record => record.Version + 1));

            if (updatedRows == 0)
            {
                return new BudgetSaveResult.StaleState();
            }

            SyncBudgetItems(budget, applicationUserId, existingItems, removedItemIds);
            context.SaveChanges();
            transaction.Commit();
            ClearChanges();
            return new BudgetSaveResult.Saved(budget);
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, BudgetNameConstraint))
        {
            transaction.Rollback();
            ClearChanges();
            return new BudgetSaveResult.DuplicateName();
        }
        catch (PostgresException exception) when (IsConstraint(exception, BudgetNameConstraint))
        {
            transaction.Rollback();
            ClearChanges();
            return new BudgetSaveResult.DuplicateName();
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, AllocationBudgetItemConstraint))
        {
            transaction.Rollback();
            ClearChanges();
            return new BudgetSaveResult.BudgetItemHasAllocations();
        }
        catch (PostgresException exception) when (IsConstraint(exception, AllocationBudgetItemConstraint))
        {
            transaction.Rollback();
            ClearChanges();
            return new BudgetSaveResult.BudgetItemHasAllocations();
        }
    }

    public bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null)
    {
        var applicationUserId = userId.Value;

        return context.Budgets.AsNoTracking().Any(budget =>
            budget.ApplicationUserId == applicationUserId
            && budget.Name == name.Value
            && budget.BudgetId != exceptBudgetId);
    }

    public IReadOnlyList<Budget> GetAll()
    {
        var applicationUserId = userId.Value;
        var budgetIds = context.Budgets
            .AsNoTracking()
            .Where(budget => budget.ApplicationUserId == applicationUserId)
            .OrderBy(budget => budget.CreatedOrder)
            .Select(budget => budget.BudgetId)
            .ToList();

        return budgetIds
            .Select(budgetId => LoadBudget(applicationUserId, budgetId))
            .OfType<Budget>()
            .ToList();
    }

    public EntityState<Budget>? Get(Guid budgetId)
    {
        var applicationUserId = userId.Value;
        var record = context.Budgets
            .AsNoTracking()
            .SingleOrDefault(budget => budget.BudgetId == budgetId && budget.ApplicationUserId == applicationUserId);

        if (record is null)
        {
            return null;
        }

        var budget = LoadBudget(applicationUserId, budgetId);
        return budget is null
            ? null
            : new PostgreSqlEntityState<Budget>(budget, record.Version);
    }

    public BudgetItem? GetBudgetItem(Guid budgetId, Guid budgetItemId)
    {
        return Get(budgetId)?.Value.GetBudgetItem(budgetItemId) is GetBudgetItemResult.Found found
            ? found.BudgetItem
            : null;
    }

    public BudgetItemReference? GetBudgetItemReference(Guid budgetItemId)
    {
        var applicationUserId = userId.Value;
        var itemRecord = context.BudgetItems
            .AsNoTracking()
            .SingleOrDefault(item => item.BudgetItemId == budgetItemId && item.ApplicationUserId == applicationUserId);

        if (itemRecord is null)
        {
            return null;
        }

        var budget = LoadBudget(applicationUserId, itemRecord.BudgetId);
        if (budget is null
            || budget.GetBudgetItem(budgetItemId) is not GetBudgetItemResult.Found found)
        {
            return null;
        }

        return new BudgetItemReference(budget.BudgetId, budget.Currency, found.BudgetItem);
    }

    private Budget? LoadBudget(Guid applicationUserId, Guid budgetId)
    {
        var budgetRecord = context.Budgets
            .AsNoTracking()
            .SingleOrDefault(budget => budget.BudgetId == budgetId && budget.ApplicationUserId == applicationUserId);

        if (budgetRecord is null)
        {
            return null;
        }

        var created = Budget.Create(
            budgetRecord.BudgetId,
            Name(budgetRecord.Name),
            Currency(budgetRecord.Currency));

        if (created is not CreateBudgetResult.Created createdBudget)
        {
            throw new InvalidOperationException("Stored budget record is invalid.");
        }

        var budget = createdBudget.Budget;
        var itemRecords = context.BudgetItems
            .AsNoTracking()
            .Where(item => item.BudgetId == budgetId && item.ApplicationUserId == applicationUserId)
            .OrderBy(item => item.CreatedOrder)
            .ToList();

        foreach (var itemRecord in itemRecords)
        {
            var added = budget.AddBudgetItem(
                itemRecord.BudgetItemId,
                Name(itemRecord.Name),
                Kind(itemRecord.Kind),
                Money(itemRecord.PlannedAmount));

            if (added is not AddBudgetItemResult.Added addedItem)
            {
                throw new InvalidOperationException("Stored budget item record is invalid.");
            }

            budget = addedItem.Budget;
        }

        return budget;
    }

    private static BudgetRecord ToRecord(Budget budget, Guid applicationUserId)
    {
        return new BudgetRecord
        {
            BudgetId = budget.BudgetId,
            ApplicationUserId = applicationUserId,
            Name = budget.Name.Value,
            Currency = budget.Currency.Value,
            Version = 1
        };
    }

    private static IEnumerable<BudgetItemRecord> ToItemRecords(Budget budget, Guid applicationUserId)
    {
        return budget.BudgetItems.Select((item, index) => ToItemRecord(budget, applicationUserId, item, index));
    }

    private static BudgetItemRecord ToItemRecord(
        Budget budget,
        Guid applicationUserId,
        BudgetItem item,
        int createdOrder)
    {
        return new BudgetItemRecord
        {
            BudgetItemId = item.BudgetItemId,
            BudgetId = budget.BudgetId,
            ApplicationUserId = applicationUserId,
            Name = item.Name.Value,
            Kind = item.Kind.Value,
            PlannedAmount = item.PlannedAmount.Value,
            Currency = budget.Currency.Value,
            CreatedOrder = createdOrder
        };
    }

    private void SyncBudgetItems(
        Budget budget,
        Guid applicationUserId,
        IReadOnlyCollection<BudgetItemRecord> existingItems,
        IReadOnlySet<Guid> removedItemIds)
    {
        var existingItemsById = existingItems.ToDictionary(item => item.BudgetItemId);
        var createdOrder = 0;

        foreach (var item in budget.BudgetItems)
        {
            if (existingItemsById.TryGetValue(item.BudgetItemId, out var itemRecord))
            {
                itemRecord.Name = item.Name.Value;
                itemRecord.Kind = item.Kind.Value;
                itemRecord.PlannedAmount = item.PlannedAmount.Value;
                itemRecord.Currency = budget.Currency.Value;
                itemRecord.CreatedOrder = createdOrder;
            }
            else
            {
                context.BudgetItems.Add(ToItemRecord(budget, applicationUserId, item, createdOrder));
            }

            createdOrder++;
        }

        context.BudgetItems.RemoveRange(existingItems.Where(item => removedItemIds.Contains(item.BudgetItemId)));
    }

    private void ClearChanges()
    {
        context.ChangeTracker.Clear();
    }

    private static bool IsConstraint(DbUpdateException exception, string constraintName)
    {
        return exception.InnerException is PostgresException postgresException
            && IsConstraint(postgresException, constraintName);
    }

    private static bool IsConstraint(PostgresException exception, string constraintName)
    {
        return exception.ConstraintName == constraintName;
    }

    private static NormalizedName Name(string value)
    {
        return NormalizedName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException("Stored name is invalid.");
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Stored currency is invalid.");
    }

    private static BudgetItemKind Kind(string value)
    {
        return BudgetItemKind.TryCreate(value, out var kind)
            ? kind
            : throw new InvalidOperationException("Stored budget item kind is invalid.");
    }

    private static PositiveMoneyAmount Money(decimal value)
    {
        return PositiveMoneyAmount.TryCreate(value.ToString("0.00", CultureInfo.InvariantCulture), out var amount)
            ? amount!
            : throw new InvalidOperationException("Stored money amount is invalid.");
    }

    private sealed class PostgreSqlEntityState<T>(T value, long version) : EntityState<T>(value)
    {
        public long Version { get; } = version;

        public override EntityState<T> Update(T value)
        {
            return new PostgreSqlEntityState<T>(value, Version);
        }
    }
}
