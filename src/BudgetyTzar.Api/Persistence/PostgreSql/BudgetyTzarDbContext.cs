using Microsoft.EntityFrameworkCore;

namespace BudgetyTzar.Api.Persistence.PostgreSql;

public sealed class BudgetyTzarDbContext(DbContextOptions<BudgetyTzarDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUserRecord> ApplicationUsers => Set<ApplicationUserRecord>();

    public DbSet<BudgetRecord> Budgets => Set<BudgetRecord>();

    public DbSet<BudgetItemRecord> BudgetItems => Set<BudgetItemRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<TransactionAllocationRecord> TransactionAllocations => Set<TransactionAllocationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("budgetytzar");
        modelBuilder.HasSequence<long>("budget_created_order");

        ConfigureApplicationUsers(modelBuilder);
        ConfigureBudgets(modelBuilder);
        ConfigureBudgetItems(modelBuilder);
        ConfigureTransactions(modelBuilder);
        ConfigureTransactionAllocations(modelBuilder);
    }

    private static void ConfigureApplicationUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUserRecord>(entity =>
        {
            entity.ToTable("application_users");

            entity.HasKey(user => user.ApplicationUserId)
                .HasName("pk_application_users");

            entity.Property(user => user.ApplicationUserId)
                .HasColumnName("application_user_id");

            entity.Property(user => user.UserKey)
                .HasColumnName("user_key")
                .HasColumnType("text")
                .IsRequired();

            entity.HasIndex(user => user.UserKey)
                .IsUnique()
                .HasDatabaseName("ux_application_users_user_key");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_application_users_user_key_not_blank",
                    "length(btrim(user_key)) > 0");
            });
        });
    }

    private static void ConfigureBudgets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BudgetRecord>(entity =>
        {
            entity.ToTable("budgets");

            entity.HasKey(budget => budget.BudgetId)
                .HasName("pk_budgets");
            entity.HasAlternateKey(budget => new { budget.BudgetId, budget.ApplicationUserId })
                .HasName("ak_budgets_id_owner");
            entity.HasAlternateKey(budget => new { budget.BudgetId, budget.ApplicationUserId, budget.Currency })
                .HasName("ak_budgets_id_owner_currency");

            entity.Property(budget => budget.BudgetId)
                .HasColumnName("budget_id");
            entity.Property(budget => budget.ApplicationUserId)
                .HasColumnName("application_user_id");
            entity.Property(budget => budget.Name)
                .HasColumnName("name")
                .HasColumnType("text")
                .IsRequired();
            entity.Property(budget => budget.Currency)
                .HasColumnName("currency")
                .HasColumnType("character(3)")
                .IsRequired();
            entity.Property(budget => budget.Version)
                .HasColumnName("version")
                .HasDefaultValue(1L)
                .IsConcurrencyToken();
            entity.Property(budget => budget.CreatedOrder)
                .HasColumnName("created_order")
                .HasDefaultValueSql("nextval('budgetytzar.budget_created_order')");

            entity.HasOne<ApplicationUserRecord>()
                .WithMany()
                .HasForeignKey(budget => budget.ApplicationUserId)
                .HasConstraintName("fk_budgets_application_users_application_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(budget => budget.ApplicationUserId)
                .HasDatabaseName("ix_budgets_application_user_id");
            entity.HasIndex(budget => budget.CreatedOrder)
                .HasDatabaseName("ix_budgets_created_order");
            entity.HasIndex(budget => new { budget.ApplicationUserId, budget.Name })
                .IsUnique()
                .HasDatabaseName("ux_budgets_application_user_id_name");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_budgets_name_not_blank", "length(btrim(name)) > 0");
                table.HasCheckConstraint("ck_budgets_currency_format", "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_budgets_version_positive", "version > 0");
                table.HasCheckConstraint("ck_budgets_created_order_non_negative", "created_order >= 0");
            });
        });
    }

    private static void ConfigureBudgetItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BudgetItemRecord>(entity =>
        {
            entity.ToTable("budget_items");

            entity.HasKey(budgetItem => budgetItem.BudgetItemId)
                .HasName("pk_budget_items");

            entity.Property(budgetItem => budgetItem.BudgetItemId)
                .HasColumnName("budget_item_id");
            entity.Property(budgetItem => budgetItem.BudgetId)
                .HasColumnName("budget_id");
            entity.Property(budgetItem => budgetItem.ApplicationUserId)
                .HasColumnName("application_user_id");
            entity.Property(budgetItem => budgetItem.Name)
                .HasColumnName("name")
                .HasColumnType("text")
                .IsRequired();
            entity.Property(budgetItem => budgetItem.Kind)
                .HasColumnName("kind")
                .HasMaxLength(16)
                .IsRequired();
            entity.Property(budgetItem => budgetItem.PlannedAmount)
                .HasColumnName("planned_amount")
                .HasPrecision(10, 2);
            entity.Property(budgetItem => budgetItem.Currency)
                .HasColumnName("currency")
                .HasColumnType("character(3)")
                .IsRequired();
            entity.Property(budgetItem => budgetItem.CreatedOrder)
                .HasColumnName("created_order");

            entity.HasAlternateKey(budgetItem => new
                {
                    budgetItem.BudgetItemId,
                    budgetItem.ApplicationUserId,
                    budgetItem.Currency
                })
                .HasName("ak_budget_items_id_owner_currency");
            entity.HasOne<ApplicationUserRecord>()
                .WithMany()
                .HasForeignKey(budgetItem => budgetItem.ApplicationUserId)
                .HasConstraintName("fk_budget_items_owner")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<BudgetRecord>()
                .WithMany()
                .HasForeignKey(budgetItem => new
                {
                    budgetItem.BudgetId,
                    budgetItem.ApplicationUserId,
                    budgetItem.Currency
                })
                .HasPrincipalKey(budget => new
                {
                    budget.BudgetId,
                    budget.ApplicationUserId,
                    budget.Currency
                })
                .HasConstraintName("fk_budget_items_budget_owner_currency")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(budgetItem => budgetItem.BudgetId)
                .HasDatabaseName("ix_budget_items_budget_id");
            entity.HasIndex(budgetItem => budgetItem.ApplicationUserId)
                .HasDatabaseName("ix_budget_items_application_user_id");
            entity.HasIndex(budgetItem => new
                {
                    budgetItem.BudgetId,
                    budgetItem.ApplicationUserId,
                    budgetItem.Currency
                })
                .HasDatabaseName("ix_budget_items_budget_owner_currency");
            entity.HasIndex(budgetItem => new { budgetItem.BudgetId, budgetItem.Name })
                .IsUnique()
                .HasDatabaseName("ux_budget_items_budget_id_name");
            entity.HasIndex(budgetItem => new { budgetItem.BudgetId, budgetItem.CreatedOrder })
                .IsUnique()
                .HasDatabaseName("ux_budget_items_budget_id_created_order");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_budget_items_name_not_blank", "length(btrim(name)) > 0");
                table.HasCheckConstraint("ck_budget_items_kind", "kind in ('Funding', 'Consumption')");
                table.HasCheckConstraint("ck_budget_items_currency_format", "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_budget_items_planned_amount_range",
                    "planned_amount > 0.00 and planned_amount <= 99999999.99");
                table.HasCheckConstraint("ck_budget_items_created_order_non_negative", "created_order >= 0");
            });
        });
    }

    private static void ConfigureTransactions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("transactions");

            entity.HasKey(transaction => transaction.TransactionId)
                .HasName("pk_transactions");
            entity.HasAlternateKey(transaction => new
                {
                    transaction.TransactionId,
                    transaction.ApplicationUserId,
                    transaction.Currency
                })
                .HasName("ak_transactions_id_owner_currency");

            entity.Property(transaction => transaction.TransactionId)
                .HasColumnName("transaction_id");
            entity.Property(transaction => transaction.ApplicationUserId)
                .HasColumnName("application_user_id");
            entity.Property(transaction => transaction.Description)
                .HasColumnName("description")
                .HasColumnType("text")
                .IsRequired();
            entity.Property(transaction => transaction.Type)
                .HasColumnName("type")
                .HasMaxLength(8)
                .IsRequired();
            entity.Property(transaction => transaction.TransactionDate)
                .HasColumnName("transaction_date")
                .HasColumnType("date");
            entity.Property(transaction => transaction.Amount)
                .HasColumnName("amount")
                .HasPrecision(10, 2);
            entity.Property(transaction => transaction.Currency)
                .HasColumnName("currency")
                .HasColumnType("character(3)")
                .IsRequired();
            entity.Property(transaction => transaction.RecordedOrder)
                .HasColumnName("recorded_order");

            entity.HasOne<ApplicationUserRecord>()
                .WithMany()
                .HasForeignKey(transaction => transaction.ApplicationUserId)
                .HasConstraintName("fk_transactions_application_users_application_user_id")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(transaction => transaction.ApplicationUserId)
                .HasDatabaseName("ix_transactions_application_user_id");
            entity.HasIndex(transaction => new { transaction.ApplicationUserId, transaction.TransactionDate })
                .HasDatabaseName("ix_transactions_application_user_id_transaction_date");
            entity.HasIndex(transaction => new { transaction.ApplicationUserId, transaction.RecordedOrder })
                .IsUnique()
                .HasDatabaseName("ux_transactions_application_user_id_recorded_order");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_transactions_description_not_blank", "length(btrim(description)) > 0");
                table.HasCheckConstraint("ck_transactions_type", "type in ('Credit', 'Debit')");
                table.HasCheckConstraint(
                    "ck_transactions_amount_range",
                    "amount > 0.00 and amount <= 99999999.99");
                table.HasCheckConstraint("ck_transactions_currency_format", "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_transactions_recorded_order_non_negative", "recorded_order >= 0");
            });
        });
    }

    private static void ConfigureTransactionAllocations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionAllocationRecord>(entity =>
        {
            entity.ToTable("transaction_allocations");

            entity.HasKey(allocation => allocation.TransactionId)
                .HasName("pk_transaction_allocations");

            entity.Property(allocation => allocation.TransactionId)
                .HasColumnName("transaction_id");
            entity.Property(allocation => allocation.ApplicationUserId)
                .HasColumnName("application_user_id");
            entity.Property(allocation => allocation.BudgetItemId)
                .HasColumnName("budget_item_id");
            entity.Property(allocation => allocation.Currency)
                .HasColumnName("currency")
                .HasColumnType("character(3)")
                .IsRequired();

            entity.HasOne<ApplicationUserRecord>()
                .WithMany()
                .HasForeignKey(allocation => allocation.ApplicationUserId)
                .HasConstraintName("fk_allocations_owner")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TransactionRecord>()
                .WithOne()
                .HasForeignKey<TransactionAllocationRecord>(allocation => new
                {
                    allocation.TransactionId,
                    allocation.ApplicationUserId,
                    allocation.Currency
                })
                .HasPrincipalKey<TransactionRecord>(transaction => new
                {
                    transaction.TransactionId,
                    transaction.ApplicationUserId,
                    transaction.Currency
                })
                .HasConstraintName("fk_allocations_transaction_owner_currency")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BudgetItemRecord>()
                .WithMany()
                .HasForeignKey(allocation => new
                {
                    allocation.BudgetItemId,
                    allocation.ApplicationUserId,
                    allocation.Currency
                })
                .HasPrincipalKey(budgetItem => new
                {
                    budgetItem.BudgetItemId,
                    budgetItem.ApplicationUserId,
                    budgetItem.Currency
                })
                .HasConstraintName("fk_allocations_budget_item_owner_currency")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(allocation => allocation.ApplicationUserId)
                .HasDatabaseName("ix_transaction_allocations_application_user_id");
            entity.HasIndex(allocation => allocation.BudgetItemId)
                .HasDatabaseName("ix_transaction_allocations_budget_item_id");
            entity.HasIndex(allocation => new
                {
                    allocation.BudgetItemId,
                    allocation.ApplicationUserId,
                    allocation.Currency
                })
                .HasDatabaseName("ix_allocations_budget_item_owner_currency");
            entity.HasIndex(allocation => new
                {
                    allocation.TransactionId,
                    allocation.ApplicationUserId,
                    allocation.Currency
                })
                .IsUnique()
                .HasDatabaseName("ux_allocations_transaction_owner_currency");

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_transaction_allocations_currency_format", "currency ~ '^[A-Z]{3}$'");
            });
        });
    }
}
