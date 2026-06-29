using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetItemKindToReportingReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "budget_snapshot_item",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Consumption");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "budget_item_projection_state",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Consumption");

            migrationBuilder.Sql("""
                UPDATE budget_item_projection_state AS projection
                SET "Kind" = item."Kind"
                FROM "BudgetItems" AS item
                WHERE projection."BudgetItemId" = item."Id";
                """);

            migrationBuilder.Sql("""
                UPDATE budget_snapshot_item AS snapshot_item
                SET "Kind" = item."Kind"
                FROM "BudgetItems" AS item
                WHERE snapshot_item."BudgetItemId" = item."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "budget_snapshot_item");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "budget_item_projection_state");
        }
    }
}
