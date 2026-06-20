using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotActivityBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualCredit",
                table: "budget_snapshot_item",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualDebit",
                table: "budget_snapshot_item",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedCredit",
                table: "budget_snapshot_item",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedDebit",
                table: "budget_snapshot_item",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalBudgetedBalance",
                table: "budget_snapshot",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTransactionBalance",
                table: "budget_snapshot",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCredit",
                table: "budget_snapshot_item");

            migrationBuilder.DropColumn(
                name: "ActualDebit",
                table: "budget_snapshot_item");

            migrationBuilder.DropColumn(
                name: "PlannedCredit",
                table: "budget_snapshot_item");

            migrationBuilder.DropColumn(
                name: "PlannedDebit",
                table: "budget_snapshot_item");

            migrationBuilder.DropColumn(
                name: "TotalBudgetedBalance",
                table: "budget_snapshot");

            migrationBuilder.DropColumn(
                name: "TotalTransactionBalance",
                table: "budget_snapshot");
        }
    }
}
