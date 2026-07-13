using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSqlSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "budgetytzar");

            migrationBuilder.CreateSequence(
                name: "budget_created_order",
                schema: "budgetytzar");

            migrationBuilder.CreateTable(
                name: "application_users",
                schema: "budgetytzar",
                columns: table => new
                {
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_key = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_users", x => x.application_user_id);
                    table.CheckConstraint("ck_application_users_user_key_not_blank", "length(btrim(user_key)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "budgetytzar",
                columns: table => new
                {
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_order = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('budgetytzar.budget_created_order')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.budget_id);
                    table.UniqueConstraint("ak_budgets_id_owner", x => new { x.budget_id, x.application_user_id });
                    table.UniqueConstraint("ak_budgets_id_owner_currency", x => new { x.budget_id, x.application_user_id, x.currency });
                    table.CheckConstraint("ck_budgets_currency_format", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_budgets_created_order_non_negative", "created_order >= 0");
                    table.CheckConstraint("ck_budgets_name_not_blank", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_budgets_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_budgets_application_users_application_user_id",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "budgetytzar",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    recorded_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.transaction_id);
                    table.UniqueConstraint("ak_transactions_id_owner_currency", x => new { x.transaction_id, x.application_user_id, x.currency });
                    table.CheckConstraint("ck_transactions_amount_range", "amount > 0.00 and amount <= 99999999.99");
                    table.CheckConstraint("ck_transactions_currency_format", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_transactions_description_not_blank", "length(btrim(description)) > 0");
                    table.CheckConstraint("ck_transactions_recorded_order_non_negative", "recorded_order >= 0");
                    table.CheckConstraint("ck_transactions_type", "type in ('Credit', 'Debit')");
                    table.ForeignKey(
                        name: "fk_transactions_application_users_application_user_id",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_items",
                schema: "budgetytzar",
                columns: table => new
                {
                    budget_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    planned_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    created_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_items", x => x.budget_item_id);
                    table.UniqueConstraint("ak_budget_items_id_owner_currency", x => new { x.budget_item_id, x.application_user_id, x.currency });
                    table.CheckConstraint("ck_budget_items_created_order_non_negative", "created_order >= 0");
                    table.CheckConstraint("ck_budget_items_currency_format", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_budget_items_kind", "kind in ('Funding', 'Consumption')");
                    table.CheckConstraint("ck_budget_items_name_not_blank", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_budget_items_planned_amount_range", "planned_amount > 0.00 and planned_amount <= 99999999.99");
                    table.ForeignKey(
                        name: "fk_budget_items_budget_owner_currency",
                        columns: x => new { x.budget_id, x.application_user_id, x.currency },
                        principalSchema: "budgetytzar",
                        principalTable: "budgets",
                        principalColumns: new[] { "budget_id", "application_user_id", "currency" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_items_owner",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transaction_allocations",
                schema: "budgetytzar",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_allocations", x => x.transaction_id);
                    table.CheckConstraint("ck_transaction_allocations_currency_format", "currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "fk_allocations_budget_item_owner_currency",
                        columns: x => new { x.budget_item_id, x.application_user_id, x.currency },
                        principalSchema: "budgetytzar",
                        principalTable: "budget_items",
                        principalColumns: new[] { "budget_item_id", "application_user_id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocations_owner",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_allocations_transaction_owner_currency",
                        columns: x => new { x.transaction_id, x.application_user_id, x.currency },
                        principalSchema: "budgetytzar",
                        principalTable: "transactions",
                        principalColumns: new[] { "transaction_id", "application_user_id", "currency" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_application_users_user_key",
                schema: "budgetytzar",
                table: "application_users",
                column: "user_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budget_items_application_user_id",
                schema: "budgetytzar",
                table: "budget_items",
                column: "application_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_items_budget_id",
                schema: "budgetytzar",
                table: "budget_items",
                column: "budget_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_items_budget_owner_currency",
                schema: "budgetytzar",
                table: "budget_items",
                columns: new[] { "budget_id", "application_user_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ux_budget_items_budget_id_created_order",
                schema: "budgetytzar",
                table: "budget_items",
                columns: new[] { "budget_id", "created_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_budget_items_budget_id_name",
                schema: "budgetytzar",
                table: "budget_items",
                columns: new[] { "budget_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budgets_application_user_id",
                schema: "budgetytzar",
                table: "budgets",
                column: "application_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_budgets_created_order",
                schema: "budgetytzar",
                table: "budgets",
                column: "created_order");

            migrationBuilder.CreateIndex(
                name: "ux_budgets_application_user_id_name",
                schema: "budgetytzar",
                table: "budgets",
                columns: new[] { "application_user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allocations_budget_item_owner_currency",
                schema: "budgetytzar",
                table: "transaction_allocations",
                columns: new[] { "budget_item_id", "application_user_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_transaction_allocations_application_user_id",
                schema: "budgetytzar",
                table: "transaction_allocations",
                column: "application_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_allocations_budget_item_id",
                schema: "budgetytzar",
                table: "transaction_allocations",
                column: "budget_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_allocations_transaction_owner_currency",
                schema: "budgetytzar",
                table: "transaction_allocations",
                columns: new[] { "transaction_id", "application_user_id", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_application_user_id",
                schema: "budgetytzar",
                table: "transactions",
                column: "application_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_application_user_id_transaction_date",
                schema: "budgetytzar",
                table: "transactions",
                columns: new[] { "application_user_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ux_transactions_application_user_id_recorded_order",
                schema: "budgetytzar",
                table: "transactions",
                columns: new[] { "application_user_id", "recorded_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_allocations",
                schema: "budgetytzar");

            migrationBuilder.DropTable(
                name: "budget_items",
                schema: "budgetytzar");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "budgetytzar");

            migrationBuilder.DropTable(
                name: "budgets",
                schema: "budgetytzar");

            migrationBuilder.DropTable(
                name: "application_users",
                schema: "budgetytzar");

            migrationBuilder.DropSequence(
                name: "budget_created_order",
                schema: "budgetytzar");
        }
    }
}
