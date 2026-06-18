using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2OutboxAndProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_audit_timeline",
                columns: table => new
                {
                    AuditEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_audit_timeline", x => x.AuditEventId);
                });

            migrationBuilder.CreateTable(
                name: "budget_line_period_summary",
                columns: table => new
                {
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RolloverType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Allocated = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReallocationIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReallocationOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsOverBudget = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_line_period_summary", x => new { x.BudgetPeriodId, x.BudgetLineId });
                });

            migrationBuilder.CreateTable(
                name: "credit_budget_line_period_summary",
                columns: table => new
                {
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BudgetLineName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PlannedCredit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualCredit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_budget_line_period_summary", x => new { x.BudgetPeriodId, x.BudgetLineId });
                });

            migrationBuilder.CreateTable(
                name: "cumulative_budget_line_balance",
                columns: table => new
                {
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cumulative_budget_line_balance", x => new { x.BudgetPeriodId, x.BudgetLineId });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: true),
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnvelopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "period_budget_summary",
                columns: table => new
                {
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedDebit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualDebit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebitRemaining = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebitVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlannedCredit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualCredit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnassignedDebitTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnassignedCreditTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PartiallyAssignedDebitTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PartiallyAssignedCreditTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_budget_summary", x => x.BudgetPeriodId);
                });

            migrationBuilder.CreateTable(
                name: "transaction_assignment_summary",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssignedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnassignedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsIgnored = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_assignment_summary", x => x.TransactionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_audit_timeline_BudgetId_BudgetPeriodId_OccurredAt",
                table: "budget_audit_timeline",
                columns: new[] { "BudgetId", "BudgetPeriodId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_line_period_summary_BudgetId_BudgetLineId",
                table: "budget_line_period_summary",
                columns: new[] { "BudgetId", "BudgetLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_budget_line_period_summary_BudgetId_StartDate",
                table: "credit_budget_line_period_summary",
                columns: new[] { "BudgetId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_cumulative_budget_line_balance_BudgetId_BudgetLineId",
                table: "cumulative_budget_line_balance",
                columns: new[] { "BudgetId", "BudgetLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_BudgetId_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "BudgetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_EventType",
                table: "OutboxMessages",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProjectedAt",
                table: "OutboxMessages",
                column: "ProjectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_period_budget_summary_BudgetId_StartDate",
                table: "period_budget_summary",
                columns: new[] { "BudgetId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_assignment_summary_BudgetId_BudgetPeriodId",
                table: "transaction_assignment_summary",
                columns: new[] { "BudgetId", "BudgetPeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_audit_timeline");

            migrationBuilder.DropTable(
                name: "budget_line_period_summary");

            migrationBuilder.DropTable(
                name: "credit_budget_line_period_summary");

            migrationBuilder.DropTable(
                name: "cumulative_budget_line_balance");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "period_budget_summary");

            migrationBuilder.DropTable(
                name: "transaction_assignment_summary");
        }
    }
}
