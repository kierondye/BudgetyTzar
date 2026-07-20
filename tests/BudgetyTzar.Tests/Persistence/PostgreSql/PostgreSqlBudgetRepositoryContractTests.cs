using System.Globalization;
using BudgetyTzar.Api.Domain.Entities;
using BudgetyTzar.Api.Domain.ValueTypes;
using BudgetyTzar.Api.Features.Budgeting;
using BudgetyTzar.Api.Features.Identity;
using BudgetyTzar.Api.Features.Transactions;
using BudgetyTzar.Api.Persistence.PostgreSql;
using BudgetyTzar.Tests.Support.Persistence;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlBudgetRepositoryContractTests(
    PostgreSqlBudgetRepositoryContractTests.DatabaseFixture database)
    : BudgetRepositoryContractTests,
        IClassFixture<PostgreSqlBudgetRepositoryContractTests.DatabaseFixture>
{
    [Fact]
    public async Task Application_user_store_uses_external_identity_as_lookup_key_for_internal_user_id()
    {
        await database.ResetAsync();
        await using var context = database.CreateContext();
        var users = new PostgreSqlApplicationUserStore(context);
        var userKey = UserKey("repository-test-user");

        var firstUserId = users.GetOrCreateApplicationUserId(userKey);
        var secondUserId = users.GetOrCreateApplicationUserId(userKey);
        var storedUser = Assert.Single(context.ApplicationUsers.AsNoTracking());

        Assert.Equal(firstUserId, secondUserId);
        Assert.Equal(firstUserId.Value, storedUser.ApplicationUserId);
        Assert.Equal(userKey.Value, storedUser.UserKey);
        Assert.NotEqual(userKey.Value, firstUserId.Value.ToString());
    }

    protected override async ValueTask<RepositoryContractContext> CreateContextAsync()
    {
        await database.ResetAsync();
        return new PostgreSqlBudgetRepositoryContractContext(database.CreateOptions());
    }

    private static ApplicationUserKey UserKey(string value)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? ApplicationUserKey.FromExternalIdentity(externalIdentity!)
            : throw new InvalidOperationException("Invalid test user.");
    }

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        public async Task InitializeAsync()
        {
            await container.StartAsync();

            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            await container.DisposeAsync();
        }

        public async Task ResetAsync()
        {
            await using var context = CreateContext();
            await context.Database.ExecuteSqlRawAsync(
                """
                truncate table
                    budgetytzar.transaction_allocations,
                    budgetytzar.budget_items,
                    budgetytzar.transactions,
                    budgetytzar.budgets,
                    budgetytzar.application_users
                restart identity cascade
                """);
        }

        public DbContextOptions<ApplicationDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(container.GetConnectionString())
                .Options;
        }

        public ApplicationDbContext CreateContext()
        {
            return new ApplicationDbContext(CreateOptions());
        }
    }
}

internal sealed class PostgreSqlBudgetRepositoryContractContext(
    DbContextOptions<ApplicationDbContext> options)
    : RepositoryContractContext
{
    private readonly List<ApplicationDbContext> contexts = [];

    public override RepositorySet ForUser(string userId)
    {
        var context = new ApplicationDbContext(options);
        contexts.Add(context);
        var userStore = new PostgreSqlApplicationUserStore(context);
        var currentUser = CurrentUser(userId, userStore);

        return new RepositorySet(
            new PostgreSqlBudgetRepository(context, currentUser),
            new PostgreSqlTestTransactionRepository(context, currentUser),
            new PostgreSqlTestTransactionAllocationRepository(context, currentUser));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }
    }

    private static CurrentUser CurrentUser(string value, PostgreSqlApplicationUserStore userStore)
    {
        return ExternalIdentity.TryCreate("BudgetyTzar.Tests", value, out var externalIdentity)
            ? new CurrentUser(userStore.GetOrCreateApplicationUserId(
                ApplicationUserKey.FromExternalIdentity(externalIdentity!)))
            : throw new InvalidOperationException("Invalid test user.");
    }
}

internal sealed class PostgreSqlTestTransactionRepository(
    ApplicationDbContext context,
    ICurrentUser currentUser)
    : ITransactionRepository
{
    public void Add(Transaction transaction)
    {
        var applicationUserId = currentUser.UserId.Value;
        var recordedOrder = context.Transactions
            .Where(record => record.ApplicationUserId == applicationUserId)
            .Select(record => (int?)record.RecordedOrder)
            .Max() + 1 ?? 0;

        context.Transactions.Add(new TransactionRecord
        {
            TransactionId = transaction.TransactionId,
            ApplicationUserId = applicationUserId,
            Description = transaction.Description,
            Type = transaction.Type.Value,
            TransactionDate = transaction.TransactionDate,
            Amount = transaction.Amount.Value,
            Currency = transaction.Currency.Value,
            RecordedOrder = recordedOrder
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    public IReadOnlyList<Transaction> GetAll()
    {
        var applicationUserId = currentUser.UserId.Value;

        return context.Transactions
            .AsNoTracking()
            .Where(record => record.ApplicationUserId == applicationUserId)
            .OrderBy(record => record.RecordedOrder)
            .ToList()
            .Select(ToTransaction)
            .ToList();
    }

    public Transaction? Get(Guid transactionId)
    {
        var applicationUserId = currentUser.UserId.Value;
        var record = context.Transactions
            .AsNoTracking()
            .SingleOrDefault(transaction =>
                transaction.TransactionId == transactionId
                && transaction.ApplicationUserId == applicationUserId);

        return record is null ? null : ToTransaction(record);
    }

    public TransactionDeleteResult Delete(Guid transactionId)
    {
        var applicationUserId = currentUser.UserId.Value;
        var record = context.Transactions
            .SingleOrDefault(transaction =>
                transaction.TransactionId == transactionId
                && transaction.ApplicationUserId == applicationUserId);

        if (record is null)
        {
            return new TransactionDeleteResult.NotFound();
        }

        if (context.TransactionAllocations.Any(allocation =>
            allocation.TransactionId == transactionId
            && allocation.ApplicationUserId == applicationUserId))
        {
            return new TransactionDeleteResult.TransactionHasAllocation();
        }

        context.Transactions.Remove(record);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return new TransactionDeleteResult.Deleted();
    }

    private static Transaction ToTransaction(TransactionRecord record)
    {
        var result = Transaction.Create(
            record.TransactionId,
            record.Description,
            TransactionType(record.Type),
            record.TransactionDate,
            Money(record.Amount),
            Currency(record.Currency));

        return result is CreateTransactionResult.Created created
            ? created.Transaction
            : throw new InvalidOperationException("Stored transaction record is invalid.");
    }

    private static TransactionType TransactionType(string value)
    {
        return BudgetyTzar.Api.Domain.ValueTypes.TransactionType.TryCreate(value, out var type)
            ? type
            : throw new InvalidOperationException("Stored transaction type is invalid.");
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Stored currency is invalid.");
    }

    private static PositiveMoneyAmount Money(decimal value)
    {
        return PositiveMoneyAmount.TryCreate(value.ToString("0.00", CultureInfo.InvariantCulture), out var amount)
            ? amount!
            : throw new InvalidOperationException("Stored money amount is invalid.");
    }
}

internal sealed class PostgreSqlTestTransactionAllocationRepository(
    ApplicationDbContext context,
    ICurrentUser currentUser)
    : ITransactionAllocationRepository
{
    public AllocateTransactionResult Allocate(TransactionAllocation allocation)
    {
        var applicationUserId = currentUser.UserId.Value;
        var transaction = context.Transactions
            .AsNoTracking()
            .SingleOrDefault(record =>
                record.TransactionId == allocation.TransactionId
                && record.ApplicationUserId == applicationUserId);

        if (transaction is null)
        {
            return new AllocateTransactionResult.TransactionNotFound();
        }

        if (!context.BudgetItems.AsNoTracking().Any(item =>
            item.BudgetItemId == allocation.BudgetItemId
            && item.ApplicationUserId == applicationUserId))
        {
            return new AllocateTransactionResult.BudgetItemNotFound();
        }

        var existingAllocation = Get(allocation.TransactionId);
        if (existingAllocation is not null)
        {
            return existingAllocation.BudgetItemId == allocation.BudgetItemId
                ? new AllocateTransactionResult.Allocated(existingAllocation)
                : new AllocateTransactionResult.AlreadyAllocatedToDifferentBudgetItem();
        }

        context.TransactionAllocations.Add(new TransactionAllocationRecord
        {
            TransactionId = allocation.TransactionId,
            ApplicationUserId = applicationUserId,
            BudgetItemId = allocation.BudgetItemId,
            Currency = transaction.Currency
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return new AllocateTransactionResult.Allocated(allocation);
    }

    public TransactionAllocation? Get(Guid transactionId)
    {
        var applicationUserId = currentUser.UserId.Value;
        var record = context.TransactionAllocations
            .AsNoTracking()
            .SingleOrDefault(allocation =>
                allocation.TransactionId == transactionId
                && allocation.ApplicationUserId == applicationUserId);

        return record is null ? null : ToAllocation(record);
    }

    public IReadOnlyList<TransactionAllocation> GetAll()
    {
        var applicationUserId = currentUser.UserId.Value;

        return context.TransactionAllocations
            .AsNoTracking()
            .Where(allocation => allocation.ApplicationUserId == applicationUserId)
            .ToList()
            .Select(ToAllocation)
            .ToList();
    }

    public RemoveTransactionAllocationResult Remove(Guid transactionId)
    {
        var applicationUserId = currentUser.UserId.Value;
        var record = context.TransactionAllocations
            .SingleOrDefault(allocation =>
                allocation.TransactionId == transactionId
                && allocation.ApplicationUserId == applicationUserId);

        if (record is null)
        {
            return new RemoveTransactionAllocationResult.NotFound();
        }

        var allocation = ToAllocation(record);
        context.TransactionAllocations.Remove(record);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return new RemoveTransactionAllocationResult.Removed(allocation);
    }

    private static TransactionAllocation ToAllocation(TransactionAllocationRecord record)
    {
        var transaction = Assert.IsType<CreateTransactionResult.Created>(
            Transaction.Create(
                record.TransactionId,
                "Allocation reference",
                BudgetyTzar.Api.Domain.ValueTypes.TransactionType.Debit,
                new DateOnly(2026, 7, 2),
                Money("1.00"),
                Currency(record.Currency))).Transaction;

        return Assert.IsType<AllocateTransactionEntityResult.Allocated>(
            TransactionAllocation.Allocate(transaction, record.BudgetItemId)).Allocation;
    }

    private static CurrencyCode Currency(string value)
    {
        return CurrencyCode.TryCreate(value, out var currency)
            ? currency
            : throw new InvalidOperationException("Stored currency is invalid.");
    }

    private static PositiveMoneyAmount Money(string value)
    {
        return PositiveMoneyAmount.TryCreate(value, out var amount)
            ? amount!
            : throw new InvalidOperationException("Invalid test amount.");
    }
}
