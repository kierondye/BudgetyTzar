using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedgerModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliesToAllPeriods = table.Column<bool>(type: "boolean", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "budget_audit_timeline",
                columns: table => new
                {
                    AuditEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_audit_timeline", x => x.AuditEventId);
                });

            migrationBuilder.CreateTable(
                name: "budget_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UnbudgetedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_snapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "budget_snapshot_item",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_snapshot_item", x => new { x.SnapshotId, x.BudgetItemId });
                });

            migrationBuilder.CreateTable(
                name: "BudgetAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReallocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LegacySignedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetReallocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromBudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToBudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetReallocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
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
                name: "processed_projection_event",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_projection_event", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "TransactionAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCandidateCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceAccount = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDuplicateCandidate = table.Column<bool>(type: "boolean", nullable: false),
                    DuplicateReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCommitted = table.Column<bool>(type: "boolean", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionImportRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceAccount = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsIgnored = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_BudgetId_AppliesToAllPeriods_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "BudgetId", "AppliesToAllPeriods", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_BudgetId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "BudgetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EntityType_EntityId",
                table: "AuditEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_audit_timeline_BudgetId_OccurredAt",
                table: "budget_audit_timeline",
                columns: new[] { "BudgetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_budget_snapshot_BudgetId_Date",
                table: "budget_snapshot",
                columns: new[] { "BudgetId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_snapshot_item_BudgetId_Date",
                table: "budget_snapshot_item",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAdjustments_BudgetId_Date",
                table: "BudgetAdjustments",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAdjustments_BudgetLineId",
                table: "BudgetAdjustments",
                column: "BudgetLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAdjustments_ReallocationId",
                table: "BudgetAdjustments",
                column: "ReallocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_BudgetId_Name",
                table: "BudgetLines",
                columns: new[] { "BudgetId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReallocations_BudgetId_Date",
                table: "BudgetReallocations",
                columns: new[] { "BudgetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_Name",
                table: "Budgets",
                column: "Name");

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
                name: "IX_processed_projection_event_BudgetId_ProcessedAt",
                table: "processed_projection_event",
                columns: new[] { "BudgetId", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAssignments_BudgetLineId",
                table: "TransactionAssignments",
                column: "BudgetLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAssignments_TransactionId",
                table: "TransactionAssignments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionImportBatches_BudgetId_CreatedAt",
                table: "TransactionImportBatches",
                columns: new[] { "BudgetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionImportRows_ImportBatchId",
                table: "TransactionImportRows",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionImportRows_TransactionId",
                table: "TransactionImportRows",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BudgetId_TransactionDate",
                table: "Transactions",
                columns: new[] { "BudgetId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ExternalReference",
                table: "Transactions",
                column: "ExternalReference");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "budget_audit_timeline");

            migrationBuilder.DropTable(
                name: "budget_snapshot");

            migrationBuilder.DropTable(
                name: "budget_snapshot_item");

            migrationBuilder.DropTable(
                name: "BudgetAdjustments");

            migrationBuilder.DropTable(
                name: "BudgetLines");

            migrationBuilder.DropTable(
                name: "BudgetReallocations");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "processed_projection_event");

            migrationBuilder.DropTable(
                name: "TransactionAssignments");

            migrationBuilder.DropTable(
                name: "TransactionImportBatches");

            migrationBuilder.DropTable(
                name: "TransactionImportRows");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
