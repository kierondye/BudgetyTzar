using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Api.Persistence.PostgreSql;
using BudgetyTzar.Tests.Support.Persistence;
using BudgetyTzar.Tests.Support.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

internal sealed class PostgreSqlRepositoryContractContext : RepositoryContractContext
{
    private readonly PostgreSqlTestDatabase database;
    private readonly List<ApplicationDbContext> contexts = [];

    private PostgreSqlRepositoryContractContext(PostgreSqlTestDatabase database)
    {
        this.database = database;
    }

    public static async ValueTask<PostgreSqlRepositoryContractContext> CreateAsync()
    {
        var database = new PostgreSqlTestDatabase();
        await database.InitializeAsync();

        var context = CreateDbContext(database.ConnectionString);
        await using (context)
        {
            await context.Database.MigrateAsync();
        }

        return new PostgreSqlRepositoryContractContext(database);
    }

    public override RepositorySet ForUser(string userId)
    {
        var context = CreateTrackedContext();
        var currentUser = CurrentUser(userId, new PostgreSqlApplicationUserStore(context));

        return new RepositorySet(
            new ContractTestBudgetRepository(context, currentUser),
            new PostgreSqlTransactionRepository(context, currentUser),
            new PostgreSqlTransactionAllocationRepository(context, currentUser));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }

        await database.DisposeAsync();
    }

    private ApplicationDbContext CreateTrackedContext()
    {
        var context = CreateDbContext(database.ConnectionString);
        contexts.Add(context);
        return context;
    }

    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static CurrentUser CurrentUser(string value, IApplicationUserStore userStore)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? new CurrentUser(userStore.GetOrCreateApplicationUserId(
                ApplicationUserKey.FromExternalIdentity(externalIdentity!)))
            : throw new InvalidOperationException("Invalid test user.");
    }

    private sealed class ContractTestBudgetRepository : IBudgetRepository
    {
        private readonly ApplicationDbContext context;
        private readonly ApplicationUserId userId;

        public ContractTestBudgetRepository(ApplicationDbContext context, ICurrentUser currentUser)
        {
            this.context = context;
            userId = currentUser.UserId;
        }

        public BudgetSaveResult Save(Budget budget)
        {
            return SaveCore(budget);
        }

        public BudgetSaveResult Save(EntityState<Budget> budgetState)
        {
            return budgetState is ContractTestEntityState state
                ? SaveCore(state.Value, state.Version)
                : new BudgetSaveResult.InvalidState();
        }

        public bool HasBudgetNamed(NormalizedName name, Guid? exceptBudgetId = null)
        {
            var applicationUserId = userId.Value;

            return context.Budgets.Any(budget =>
                budget.ApplicationUserId == applicationUserId
                && budget.Name == name.Value
                && budget.BudgetId != exceptBudgetId);
        }

        public IReadOnlyList<Budget> GetAll()
        {
            var applicationUserId = userId.Value;

            return context.Budgets
                .AsNoTracking()
                .Where(budget => budget.ApplicationUserId == applicationUserId)
                .OrderBy(budget => budget.Name)
                .Select(budget => ToBudget(
                    budget,
                    context.BudgetItems
                        .AsNoTracking()
                        .Where(item => item.BudgetId == budget.BudgetId)
                        .OrderBy(item => item.CreatedOrder)
                        .ToList()))
                .ToList();
        }

        public EntityState<Budget>? Get(Guid budgetId)
        {
            var applicationUserId = userId.Value;

            var budget = context.Budgets
                .AsNoTracking()
                .SingleOrDefault(budget =>
                    budget.BudgetId == budgetId
                    && budget.ApplicationUserId == applicationUserId);

            if (budget is null)
            {
                return null;
            }

            var budgetItems = context.BudgetItems
                .AsNoTracking()
                .Where(budgetItem => budgetItem.BudgetId == budgetId)
                .OrderBy(budgetItem => budgetItem.CreatedOrder)
                .ToList();

            return new ContractTestEntityState(ToBudget(budget, budgetItems), budget.Version);
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

            var record = context.BudgetItems
                .AsNoTracking()
                .SingleOrDefault(budgetItem =>
                    budgetItem.BudgetItemId == budgetItemId
                    && budgetItem.ApplicationUserId == applicationUserId);

            if (record is null)
            {
                return null;
            }

            var budget = context.Budgets
                .AsNoTracking()
                .Single(existing => existing.BudgetId == record.BudgetId);

            return new BudgetItemReference(
                record.BudgetId,
                Currency(budget.Currency),
                ToBudgetItem(record));
        }

        private BudgetSaveResult SaveCore(Budget budget, long? expectedVersion = null)
        {
            var applicationUserId = userId.Value;
            var existing = context.Budgets.SingleOrDefault(existingBudget =>
                existingBudget.BudgetId == budget.BudgetId
                && existingBudget.ApplicationUserId == applicationUserId);

            if (expectedVersion.HasValue && existing is null)
            {
                return new BudgetSaveResult.NotFound();
            }

            if (!expectedVersion.HasValue && existing is not null)
            {
                return new BudgetSaveResult.DuplicateIdentity();
            }

            if (expectedVersion.HasValue && existing!.Version != expectedVersion.Value)
            {
                return new BudgetSaveResult.StaleState();
            }

            if (context.Budgets.Any(existingBudget =>
                existingBudget.ApplicationUserId == applicationUserId
                && existingBudget.Name == budget.Name.Value
                && existingBudget.BudgetId != budget.BudgetId))
            {
                return new BudgetSaveResult.DuplicateName();
            }

            if (existing is null)
            {
                context.Budgets.Add(new BudgetRecord
                {
                    BudgetId = budget.BudgetId,
                    ApplicationUserId = applicationUserId,
                    Name = budget.Name.Value,
                    Currency = budget.Currency.Value,
                    Version = 1
                });
            }
            else
            {
                existing.Name = budget.Name.Value;
                existing.Version++;
            }

            var existingItems = context.BudgetItems
                .Where(item => item.BudgetId == budget.BudgetId && item.ApplicationUserId == applicationUserId)
                .ToList();
            var updatedItemIds = budget.BudgetItems
                .Select(item => item.BudgetItemId)
                .ToHashSet();

            context.BudgetItems.RemoveRange(
                existingItems.Where(existingItem => !updatedItemIds.Contains(existingItem.BudgetItemId)));

            var createdOrder = 0;
            foreach (var budgetItem in budget.BudgetItems)
            {
                var existingItem = existingItems.SingleOrDefault(item => item.BudgetItemId == budgetItem.BudgetItemId);

                if (existingItem is null)
                {
                    context.BudgetItems.Add(new BudgetItemRecord
                    {
                        BudgetItemId = budgetItem.BudgetItemId,
                        BudgetId = budget.BudgetId,
                        ApplicationUserId = applicationUserId,
                        Name = budgetItem.Name.Value,
                        Kind = budgetItem.Kind.Value,
                        PlannedAmount = budgetItem.PlannedAmount.Value,
                        Currency = budget.Currency.Value,
                        CreatedOrder = createdOrder
                    });
                }
                else
                {
                    existingItem.Name = budgetItem.Name.Value;
                    existingItem.Kind = budgetItem.Kind.Value;
                    existingItem.PlannedAmount = budgetItem.PlannedAmount.Value;
                    existingItem.CreatedOrder = createdOrder;
                }

                createdOrder++;
            }

            context.SaveChanges();

            return new BudgetSaveResult.Saved(budget);
        }

        private static Budget ToBudget(BudgetRecord record, IReadOnlyList<BudgetItemRecord> budgetItems)
        {
            var budget = Assert.IsType<CreateBudgetResult.Created>(
                Budget.Create(record.BudgetId, Name(record.Name), Currency(record.Currency))).Budget;

            foreach (var budgetItem in budgetItems)
            {
                budget = Assert.IsType<AddBudgetItemResult.Added>(
                    budget.AddBudgetItem(
                        budgetItem.BudgetItemId,
                        Name(budgetItem.Name),
                        Kind(budgetItem.Kind),
                        Money(budgetItem.PlannedAmount))).Budget;
            }

            return budget;
        }

        private static BudgetItem ToBudgetItem(BudgetItemRecord record)
        {
            return Assert.IsType<CreateBudgetItemEntityResult.Created>(
                BudgetItem.Create(
                    record.BudgetItemId,
                    Name(record.Name),
                    Kind(record.Kind),
                    Money(record.PlannedAmount))).BudgetItem;
        }

        private static NormalizedName Name(string value)
        {
            return NormalizedName.TryCreate(value, out var name)
                ? name
                : throw new InvalidOperationException("Invalid stored budget name.");
        }

        private static CurrencyCode Currency(string value)
        {
            return CurrencyCode.TryCreate(value, out var currency)
                ? currency
                : throw new InvalidOperationException("Invalid stored currency.");
        }

        private static BudgetItemKind Kind(string value)
        {
            return BudgetItemKind.TryCreate(value, out var kind)
                ? kind
                : throw new InvalidOperationException("Invalid stored budget item kind.");
        }

        private static PositiveMoneyAmount Money(decimal value)
        {
            return PositiveMoneyAmount.TryCreate(
                value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                out var amount)
                ? amount!
                : throw new InvalidOperationException("Invalid stored amount.");
        }
    }

    private sealed class ContractTestEntityState(Budget value, long version) : EntityState<Budget>(value)
    {
        public long Version { get; } = version;

        public override EntityState<Budget> Update(Budget value)
        {
            return new ContractTestEntityState(value, Version);
        }
    }
}
