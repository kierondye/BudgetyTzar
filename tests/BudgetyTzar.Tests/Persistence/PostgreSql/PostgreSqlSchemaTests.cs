using System.Data.Common;
using BudgetyTzar.Api.Persistence.PostgreSql;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BudgetyTzar.Tests.Persistence.PostgreSql;

public sealed class PostgreSqlSchemaTests(PostgreSqlSchemaTests.DatabaseFixture database)
    : IClassFixture<PostgreSqlSchemaTests.DatabaseFixture>
{
    [Fact]
    public async Task Fresh_postgresql_database_can_be_migrated_to_current_schema()
    {
        await using var context = database.CreateContext();

        var tables = await ReadStringsAsync(
            context,
            """
            select table_name
            from information_schema.tables
            where table_schema = 'budgetytzar'
            order by table_name
            """);

        Assert.Equal(
            [
                "application_users",
                "audit_records",
                "budget_items",
                "budgets",
                "transaction_allocations",
                "transactions"
            ],
            tables);
    }

    [Fact]
    public async Task Monetary_columns_use_decimal_precision_and_scale()
    {
        await using var context = database.CreateContext();

        var columns = await ReadStringsAsync(
            context,
            """
            select table_name || '.' || column_name || ':' || numeric_precision || ',' || numeric_scale
            from information_schema.columns
            where table_schema = 'budgetytzar'
              and column_name in ('amount', 'planned_amount')
            order by table_name, column_name
            """);

        Assert.Equal(
            [
                "budget_items.planned_amount:10,2",
                "transactions.amount:10,2"
            ],
            columns);
    }

    [Fact]
    public async Task Allocation_amount_is_derived_from_the_referenced_transaction()
    {
        await using var context = database.CreateContext();

        var allocationColumns = await ReadStringsAsync(
            context,
            """
            select column_name
            from information_schema.columns
            where table_schema = 'budgetytzar'
              and table_name = 'transaction_allocations'
            order by column_name
            """);

        Assert.DoesNotContain("amount", allocationColumns);
    }

    [Fact]
    public async Task Schema_defines_key_constraints_for_persisted_invariants()
    {
        await using var context = database.CreateContext();

        var constraints = await ReadStringsAsync(
            context,
            """
            select constraint_name
            from information_schema.table_constraints
            where table_schema = 'budgetytzar'
            order by constraint_name
            """);

        Assert.Contains("pk_application_users", constraints);
        Assert.Contains("pk_audit_records", constraints);
        Assert.Contains("pk_budgets", constraints);
        Assert.Contains("pk_budget_items", constraints);
        Assert.Contains("pk_transactions", constraints);
        Assert.Contains("pk_transaction_allocations", constraints);
        Assert.Contains("fk_budget_items_budget_owner_currency", constraints);
        Assert.Contains("fk_budgets_application_users_application_user_id", constraints);
        Assert.Contains("fk_allocations_budget_item_owner_currency", constraints);
        Assert.Contains("fk_allocations_transaction_owner_currency", constraints);
        Assert.Contains("fk_transactions_application_users_application_user_id", constraints);
        Assert.Contains("fk_audit_records_application_users_application_user_id", constraints);
        Assert.DoesNotContain("fk_audit_records_actor_application_user_id", constraints);
        Assert.Contains("ck_audit_records_action_not_blank", constraints);
        Assert.Contains("ck_audit_records_correlation_id_not_blank", constraints);
        Assert.Contains("ck_audit_records_operation_name_not_blank", constraints);
        Assert.Contains("ck_budgets_created_order_non_negative", constraints);
        Assert.Contains("ck_budget_items_planned_amount_range", constraints);
        Assert.Contains("ck_transactions_amount_range", constraints);
        Assert.Contains("ck_transaction_allocations_currency_format", constraints);
    }

    [Fact]
    public async Task Schema_defines_ownership_and_relationship_lookup_indexes()
    {
        await using var context = database.CreateContext();

        var indexes = await ReadStringsAsync(
            context,
            """
            select indexname
            from pg_indexes
            where schemaname = 'budgetytzar'
            order by indexname
            """);

        Assert.Contains("ix_budgets_application_user_id", indexes);
        Assert.Contains("ix_audit_records_application_user_id_action", indexes);
        Assert.Contains("ix_audit_records_application_user_id_persisted_at_utc", indexes);
        Assert.Contains("ix_audit_records_correlation_id", indexes);
        Assert.Contains("ix_budgets_created_order", indexes);
        Assert.Contains("ix_budget_items_budget_id", indexes);
        Assert.Contains("ix_budget_items_budget_owner_currency", indexes);
        Assert.Contains("ix_allocations_budget_item_owner_currency", indexes);
        Assert.Contains("ix_transaction_allocations_application_user_id", indexes);
        Assert.Contains("ix_transaction_allocations_budget_item_id", indexes);
        Assert.Contains("ix_transactions_application_user_id", indexes);
        Assert.Contains("ix_transactions_application_user_id_transaction_date", indexes);
        Assert.Contains("ux_application_users_user_key", indexes);
        Assert.Contains("ux_allocations_transaction_owner_currency", indexes);
        Assert.Contains("ux_budgets_application_user_id_name", indexes);
        Assert.Contains("ux_budget_items_budget_id_name", indexes);
        Assert.Contains("ux_transactions_application_user_id_recorded_order", indexes);
    }

    [Fact]
    public async Task Storage_constraints_enforce_budget_and_allocation_uniqueness()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var budgetA = Guid.NewGuid();
        var budgetB = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var firstBudgetItemId = Guid.NewGuid();
        var secondBudgetItemId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.ApplicationUsers.AddRange(
                new ApplicationUserRecord { ApplicationUserId = userA, UserKey = "provider:user-a" },
                new ApplicationUserRecord { ApplicationUserId = userB, UserKey = "provider:user-b" });
            context.Budgets.AddRange(
                Budget(budgetA, userA, "UK", "GBP"),
                Budget(budgetB, userB, "UK", "EUR"));
            context.BudgetItems.AddRange(
                BudgetItem(firstBudgetItemId, budgetA, userA, "Groceries", "GBP", 0),
                BudgetItem(secondBudgetItemId, budgetA, userA, "Transport", "GBP", 1));
            context.Transactions.Add(Transaction(transactionId, userA, 0));
            context.TransactionAllocations.Add(Allocation(transactionId, userA, firstBudgetItemId));

            await context.SaveChangesAsync();
        }

        await AssertConstraintFailureAsync(context =>
        {
            context.Budgets.Add(Budget(Guid.NewGuid(), userA, "UK", "GBP"));
        });
        await AssertConstraintFailureAsync(context =>
        {
            context.BudgetItems.Add(BudgetItem(Guid.NewGuid(), budgetA, userA, "Groceries", "GBP", 2));
        });
        await AssertConstraintFailureAsync(context =>
        {
            context.TransactionAllocations.Add(Allocation(transactionId, userA, secondBudgetItemId));
        });
        await AssertConstraintFailureAsync(context =>
        {
            context.TransactionAllocations.Add(Allocation(Guid.NewGuid(), userA, firstBudgetItemId));
        });
    }

    [Fact]
    public async Task Storage_constraints_reject_cross_owner_allocations()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userABudgetId = Guid.NewGuid();
        var userBBudgetId = Guid.NewGuid();
        var userATransactionId = Guid.NewGuid();
        var userBTransactionId = Guid.NewGuid();
        var userABudgetItemId = Guid.NewGuid();
        var userBBudgetItemId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.ApplicationUsers.AddRange(
                new ApplicationUserRecord { ApplicationUserId = userA, UserKey = "owner:user-a" },
                new ApplicationUserRecord { ApplicationUserId = userB, UserKey = "owner:user-b" });
            context.Budgets.AddRange(
                Budget(userABudgetId, userA, "User A", "GBP"),
                Budget(userBBudgetId, userB, "User B", "GBP"));
            context.BudgetItems.AddRange(
                BudgetItem(userABudgetItemId, userABudgetId, userA, "Groceries", "GBP", 0),
                BudgetItem(userBBudgetItemId, userBBudgetId, userB, "Groceries", "GBP", 0));
            context.Transactions.AddRange(
                Transaction(userATransactionId, userA, 0),
                Transaction(userBTransactionId, userB, 0));

            await context.SaveChangesAsync();
        }

        await AssertConstraintFailureAsync(context =>
        {
            context.TransactionAllocations.Add(Allocation(userBTransactionId, userA, userABudgetItemId));
        });
        await AssertConstraintFailureAsync(context =>
        {
            context.TransactionAllocations.Add(Allocation(userATransactionId, userA, userBBudgetItemId));
        });
    }

    [Fact]
    public async Task Storage_constraints_reject_budget_item_and_allocation_currency_drift()
    {
        var userId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var budgetItemId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.ApplicationUsers.Add(new ApplicationUserRecord { ApplicationUserId = userId, UserKey = "currency:user" });
            context.Budgets.Add(Budget(budgetId, userId, "UK", "GBP"));
            context.BudgetItems.Add(BudgetItem(budgetItemId, budgetId, userId, "Groceries", "GBP", 0));
            context.Transactions.Add(Transaction(transactionId, userId, 0));

            await context.SaveChangesAsync();
        }

        await AssertConstraintFailureAsync(context =>
        {
            context.BudgetItems.Add(BudgetItem(Guid.NewGuid(), budgetId, userId, "Transport", "EUR", 1));
        });
        await AssertConstraintFailureAsync(context =>
        {
            context.TransactionAllocations.Add(Allocation(transactionId, userId, budgetItemId, "EUR"));
        });
    }

    private async Task AssertConstraintFailureAsync(Action<ApplicationDbContext> arrange)
    {
        await using var context = database.CreateContext();
        arrange(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static BudgetRecord Budget(Guid budgetId, Guid userId, string name, string currency)
    {
        return new BudgetRecord
        {
            BudgetId = budgetId,
            ApplicationUserId = userId,
            Name = name,
            Currency = currency,
            Version = 1
        };
    }

    private static BudgetItemRecord BudgetItem(
        Guid budgetItemId,
        Guid budgetId,
        Guid userId,
        string name,
        string currency,
        int createdOrder)
    {
        return new BudgetItemRecord
        {
            BudgetItemId = budgetItemId,
            BudgetId = budgetId,
            ApplicationUserId = userId,
            Name = name,
            Kind = "Consumption",
            PlannedAmount = 400.00m,
            Currency = currency,
            CreatedOrder = createdOrder
        };
    }

    private static TransactionRecord Transaction(Guid transactionId, Guid userId, int recordedOrder)
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

    private static TransactionAllocationRecord Allocation(
        Guid transactionId,
        Guid userId,
        Guid budgetItemId,
        string currency = "GBP")
    {
        return new TransactionAllocationRecord
        {
            TransactionId = transactionId,
            ApplicationUserId = userId,
            BudgetItemId = budgetItemId,
            Currency = currency
        };
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(ApplicationDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
        }

        var values = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
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

        private string ConnectionString => container.GetConnectionString();

        public ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
