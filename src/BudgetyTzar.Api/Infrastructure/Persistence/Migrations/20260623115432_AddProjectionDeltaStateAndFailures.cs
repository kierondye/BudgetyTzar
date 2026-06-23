using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionDeltaStateAndFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_adjustment_projection_state",
                columns: table => new
                {
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_adjustment_projection_state", x => x.ActivityId);
                });

            migrationBuilder.CreateTable(
                name: "budget_item_projection_state",
                columns: table => new
                {
                    BudgetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_item_projection_state", x => x.BudgetItemId);
                });

            migrationBuilder.CreateTable(
                name: "projection_event_failure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Topic = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Partition = table.Column<int>(type: "integer", nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    ConsumerGroup = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RawEventJson = table.Column<string>(type: "text", nullable: false),
                    FirstFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_event_failure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transaction_allocation_projection_state",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_allocation_projection_state", x => new { x.TransactionId, x.BudgetItemId });
                });

            migrationBuilder.CreateTable(
                name: "transaction_projection_state",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsIgnored = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_projection_state", x => x.TransactionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_adjustment_projection_state_BudgetId_Date",
                table: "budget_adjustment_projection_state",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_adjustment_projection_state_BudgetItemId",
                table: "budget_adjustment_projection_state",
                column: "BudgetItemId");

            migrationBuilder.CreateIndex(
                name: "IX_budget_adjustment_projection_state_SourceEventId",
                table: "budget_adjustment_projection_state",
                column: "SourceEventId");

            migrationBuilder.CreateIndex(
                name: "IX_budget_item_projection_state_BudgetId_Name",
                table: "budget_item_projection_state",
                columns: new[] { "BudgetId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_projection_event_failure_EventId",
                table: "projection_event_failure",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_projection_event_failure_Status_LastFailedAt",
                table: "projection_event_failure",
                columns: new[] { "Status", "LastFailedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_projection_event_failure_Topic_Partition_Offset",
                table: "projection_event_failure",
                columns: new[] { "Topic", "Partition", "Offset" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_allocation_projection_state_BudgetId_BudgetItem~",
                table: "transaction_allocation_projection_state",
                columns: new[] { "BudgetId", "BudgetItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_projection_state_BudgetId_TransactionDate",
                table: "transaction_projection_state",
                columns: new[] { "BudgetId", "TransactionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_adjustment_projection_state");

            migrationBuilder.DropTable(
                name: "budget_item_projection_state");

            migrationBuilder.DropTable(
                name: "projection_event_failure");

            migrationBuilder.DropTable(
                name: "transaction_allocation_projection_state");

            migrationBuilder.DropTable(
                name: "transaction_projection_state");
        }
    }
}
