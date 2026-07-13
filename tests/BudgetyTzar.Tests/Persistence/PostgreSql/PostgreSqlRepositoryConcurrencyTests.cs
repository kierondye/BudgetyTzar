using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Api.Persistence.PostgreSql;
using BudgetyTzar.Tests.Support.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlRepositoryConcurrencyTests
{
    [Fact]
    public async Task Application_user_store_uses_concurrently_created_application_user()
    {
        await using var database = await CreateDatabaseAsync();
        var userKey = UserKey("postgres-race-user");
        var concurrentUserId = Guid.NewGuid();
        var interceptor = new BeforeSaveInterceptor(
            context => HasAdded<ApplicationUserRecord>(context, user => user.UserKey == userKey.Value),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                concurrentContext.ApplicationUsers.Add(new ApplicationUserRecord
                {
                    ApplicationUserId = concurrentUserId,
                    UserKey = userKey.Value
                });
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var users = new PostgreSqlApplicationUserStore(context);

        var userId = users.GetOrCreateApplicationUserId(userKey);

        Assert.Equal(concurrentUserId, userId.Value);
    }

    [Fact]
    public async Task Transaction_add_retries_when_concurrent_recording_claims_recorded_order()
    {
        await using var database = await CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        var userKey = "postgres-created-order-race-user";
        var existingTransactionId = Guid.NewGuid();
        var concurrentTransactionId = Guid.NewGuid();
        await SeedBudgetItemAndTransactionAsync(database.ConnectionString, userId, userKey, existingTransactionId);
        var interceptor = new BeforeSaveInterceptor(
            context => HasAdded<TransactionRecord>(context, transaction => transaction.RecordedOrder == 1),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                concurrentContext.Transactions.Add(TransactionRecord(concurrentTransactionId, userId, recordedOrder: 1));
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new PostgreSqlTransactionRepository(context, CurrentUser(userKey, database.ConnectionString));
        var transaction = CreateTransaction();

        repository.Add(transaction);

        await using var assertionContext = CreateContext(database.ConnectionString);
        Assert.Equal(
            [
                existingTransactionId,
                concurrentTransactionId,
                transaction.TransactionId
            ],
            await assertionContext.Transactions
                .OrderBy(storedTransaction => storedTransaction.RecordedOrder)
                .Select(storedTransaction => storedTransaction.TransactionId)
                .ToListAsync());
    }

    [Fact]
    public async Task Transaction_delete_returns_allocated_result_when_concurrent_allocation_wins()
    {
        await using var database = await CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        var userKey = "postgres-delete-race-user";
        var transactionId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        await SeedBudgetItemAndTransactionAsync(database.ConnectionString, userId, userKey, transactionId, budgetItemId);
        var interceptor = new BeforeSaveInterceptor(
            context => HasDeleted<TransactionRecord>(context, transaction => transaction.TransactionId == transactionId),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                concurrentContext.TransactionAllocations.Add(Allocation(transactionId, userId, budgetItemId));
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new PostgreSqlTransactionRepository(context, CurrentUser(userKey, database.ConnectionString));

        var result = repository.Delete(transactionId);

        Assert.IsType<TransactionDeleteResult.TransactionHasAllocation>(result);
    }

    [Fact]
    public async Task Allocation_create_returns_existing_allocation_when_concurrent_same_budget_item_wins()
    {
        await using var database = await CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        var userKey = "postgres-allocation-same-race-user";
        var transactionId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        await SeedBudgetItemAndTransactionAsync(database.ConnectionString, userId, userKey, transactionId, budgetItemId);
        var interceptor = new BeforeSaveInterceptor(
            context => HasAdded<TransactionAllocationRecord>(context, allocation => allocation.TransactionId == transactionId),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                concurrentContext.TransactionAllocations.Add(Allocation(transactionId, userId, budgetItemId));
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new PostgreSqlTransactionAllocationRepository(context, CurrentUser(userKey, database.ConnectionString));
        var allocation = CreateAllocation(CreateTransaction(transactionId), budgetItemId);

        var result = repository.Allocate(allocation);

        var allocated = Assert.IsType<AllocateTransactionResult.Allocated>(result);
        Assert.Equal(budgetItemId, allocated.Allocation.BudgetItemId);
    }

    [Fact]
    public async Task Allocation_create_returns_conflict_when_concurrent_different_budget_item_wins()
    {
        await using var database = await CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        var userKey = "postgres-allocation-different-race-user";
        var transactionId = Guid.NewGuid();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();
        await SeedBudgetItemAndTransactionAsync(
            database.ConnectionString,
            userId,
            userKey,
            transactionId,
            firstBudgetItemId,
            secondBudgetItemId);
        var interceptor = new BeforeSaveInterceptor(
            context => HasAdded<TransactionAllocationRecord>(context, allocation => allocation.TransactionId == transactionId),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                concurrentContext.TransactionAllocations.Add(Allocation(transactionId, userId, firstBudgetItemId));
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new PostgreSqlTransactionAllocationRepository(context, CurrentUser(userKey, database.ConnectionString));
        var allocation = CreateAllocation(CreateTransaction(transactionId), secondBudgetItemId);

        var result = repository.Allocate(allocation);

        Assert.IsType<AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem>(result);
    }

    [Fact]
    public async Task Allocation_remove_is_noop_when_concurrent_remove_wins()
    {
        await using var database = await CreateDatabaseAsync();
        var userId = Guid.NewGuid();
        var userKey = "postgres-remove-race-user";
        var transactionId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();
        await SeedBudgetItemAndTransactionAsync(database.ConnectionString, userId, userKey, transactionId, budgetItemId);
        await using (var seedContext = CreateContext(database.ConnectionString))
        {
            seedContext.TransactionAllocations.Add(Allocation(transactionId, userId, budgetItemId));
            await seedContext.SaveChangesAsync();
        }

        var interceptor = new BeforeSaveInterceptor(
            context => HasDeleted<TransactionAllocationRecord>(context, allocation => allocation.TransactionId == transactionId),
            connectionString =>
            {
                using var concurrentContext = CreateContext(connectionString);
                var allocation = concurrentContext.TransactionAllocations.Single(allocation =>
                    allocation.TransactionId == transactionId
                    && allocation.ApplicationUserId == userId);
                concurrentContext.TransactionAllocations.Remove(allocation);
                concurrentContext.SaveChanges();
            });
        await using var context = CreateContext(database.ConnectionString, interceptor);
        var repository = new PostgreSqlTransactionAllocationRepository(context, CurrentUser(userKey, database.ConnectionString));

        repository.Remove(transactionId);

        Assert.Null(repository.Get(transactionId));
    }

    private static async Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        var database = new PostgreSqlTestDatabase();
        await database.InitializeAsync();

        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();

        return database;
    }

    private static async Task SeedBudgetItemAndTransactionAsync(
        string connectionString,
        Guid userId,
        string userKey,
        Guid transactionId,
        params Guid[] budgetItemIds)
    {
        await using var context = CreateContext(connectionString);
        var budgetId = Guid.NewGuid();

        context.ApplicationUsers.Add(new ApplicationUserRecord
        {
            ApplicationUserId = userId,
            UserKey = UserKey(userKey).Value
        });
        context.Budgets.Add(new BudgetRecord
        {
            BudgetId = budgetId,
            ApplicationUserId = userId,
            Name = "UK",
            Currency = "GBP",
            Version = 1
        });

        for (var index = 0; index < budgetItemIds.Length; index++)
        {
            context.BudgetItems.Add(new BudgetItemRecord
            {
                BudgetItemId = budgetItemIds[index],
                BudgetId = budgetId,
                ApplicationUserId = userId,
                Name = $"Budget Item {index}",
                Kind = "Consumption",
                PlannedAmount = 400.00m,
                Currency = "GBP",
                CreatedOrder = index
            });
        }

        context.Transactions.Add(TransactionRecord(transactionId, userId));

        await context.SaveChangesAsync();
    }

    private static BudgetyTzarDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<BudgetyTzarDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options;

        return new BudgetyTzarDbContext(options);
    }

    private static bool HasAdded<TRecord>(BudgetyTzarDbContext context, Func<TRecord, bool> predicate)
        where TRecord : class
    {
        return HasEntry(context, EntityState.Added, predicate);
    }

    private static bool HasDeleted<TRecord>(BudgetyTzarDbContext context, Func<TRecord, bool> predicate)
        where TRecord : class
    {
        return HasEntry(context, EntityState.Deleted, predicate);
    }

    private static bool HasEntry<TRecord>(
        BudgetyTzarDbContext context,
        EntityState state,
        Func<TRecord, bool> predicate)
        where TRecord : class
    {
        return context.ChangeTracker.Entries<TRecord>()
            .Any(entry => entry.State == state && predicate(entry.Entity));
    }

    private static CurrentUser CurrentUser(string value, string connectionString)
    {
        using var context = CreateContext(connectionString);
        var users = new PostgreSqlApplicationUserStore(context);
        return new CurrentUser(users.GetOrCreateApplicationUserId(UserKey(value)));
    }

    private static ApplicationUserKey UserKey(string value)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? ApplicationUserKey.FromExternalIdentity(externalIdentity!)
            : throw new InvalidOperationException("Invalid test user.");
    }

    private static Transaction CreateTransaction(Guid? transactionId = null)
    {
        return Assert.IsType<CreateTransactionResult.Created>(
            Transaction.Create(
                transactionId ?? Guid.NewGuid(),
                "Groceries",
                TransactionType.Debit,
                new DateOnly(2026, 7, 2),
                Money("42.50"),
                Currency("GBP"))).Transaction;
    }

    private static TransactionAllocation CreateAllocation(Transaction transaction, Guid budgetItemId)
    {
        return Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, budgetItemId)).Allocation;
    }

    private static TransactionRecord TransactionRecord(Guid transactionId, Guid userId, int recordedOrder = 0)
    {
        return new TransactionRecord
        {
            TransactionId = transactionId,
            ApplicationUserId = userId,
            Description = "Groceries",
            Type = "Debit",
            TransactionDate = new DateOnly(2026, 7, 2),
            Amount = 42.50m,
            Currency = "GBP",
            RecordedOrder = recordedOrder
        };
    }

    private static TransactionAllocationRecord Allocation(Guid transactionId, Guid userId, Guid budgetItemId)
    {
        return new TransactionAllocationRecord
        {
            TransactionId = transactionId,
            ApplicationUserId = userId,
            BudgetItemId = budgetItemId,
            Currency = "GBP"
        };
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Invalid test currency.");
    }

    private static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var amount)
            ? amount!
            : throw new InvalidOperationException("Invalid test amount.");
    }

    private sealed class BeforeSaveInterceptor(
        Func<BudgetyTzarDbContext, bool> shouldRun,
        Action<string> action) : SaveChangesInterceptor
    {
        private bool hasRun;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (!hasRun
                && eventData.Context is BudgetyTzarDbContext context
                && shouldRun(context))
            {
                hasRun = true;
                action(context.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("A test connection string is required."));
            }

            return result;
        }
    }
}
