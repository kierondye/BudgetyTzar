using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2LedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BudgetId",
                table: "BudgetReallocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "BudgetReallocations",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BudgetReallocations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BudgetId",
                table: "BudgetAdjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "BudgetAdjustments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "LegacySignedAmount",
                table: "BudgetAdjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BudgetAdjustments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReallocationId",
                table: "BudgetAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BudgetAdjustments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "BudgetAdjustments" AS adjustment
                SET
                    "BudgetId" = period."BudgetId",
                    "Date" = period."StartDate",
                    "LegacySignedAmount" = adjustment."Amount",
                    "Amount" = abs(adjustment."Amount"),
                    "Type" = CASE WHEN adjustment."Amount" < 0 THEN 'Debit' ELSE 'Credit' END,
                    "Notes" = adjustment."Reason"
                FROM "BudgetPeriods" AS period
                WHERE adjustment."BudgetPeriodId" = period."Id";
                """);

            migrationBuilder.Sql("""
                UPDATE "BudgetReallocations" AS reallocation
                SET
                    "BudgetId" = period."BudgetId",
                    "Date" = period."StartDate",
                    "Notes" = reallocation."Reason"
                FROM "BudgetPeriods" AS period
                WHERE reallocation."BudgetPeriodId" = period."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReallocations_BudgetId_Date",
                table: "BudgetReallocations",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAdjustments_BudgetId_Date",
                table: "BudgetAdjustments",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAdjustments_ReallocationId",
                table: "BudgetAdjustments",
                column: "ReallocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BudgetReallocations_BudgetId_Date",
                table: "BudgetReallocations");

            migrationBuilder.DropIndex(
                name: "IX_BudgetAdjustments_BudgetId_Date",
                table: "BudgetAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_BudgetAdjustments_ReallocationId",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "BudgetId",
                table: "BudgetReallocations");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "BudgetReallocations");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BudgetReallocations");

            migrationBuilder.DropColumn(
                name: "BudgetId",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "LegacySignedAmount",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "ReallocationId",
                table: "BudgetAdjustments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BudgetAdjustments");
        }
    }
}
