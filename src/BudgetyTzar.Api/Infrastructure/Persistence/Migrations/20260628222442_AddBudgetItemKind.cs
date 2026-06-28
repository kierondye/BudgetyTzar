using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetItemKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "BudgetItems",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Consumption");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BudgetItems");
        }
    }
}
