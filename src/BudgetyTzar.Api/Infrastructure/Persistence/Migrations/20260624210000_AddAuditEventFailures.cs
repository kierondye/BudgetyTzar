using System;
using BudgetyTzar.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(BudgetDbContext))]
    [Migration("20260624210000_AddAuditEventFailures")]
    public partial class AddAuditEventFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_event_failure",
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
                    table.PrimaryKey("PK_audit_event_failure", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_failure_EventId",
                table: "audit_event_failure",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_failure_Status_LastFailedAt",
                table: "audit_event_failure",
                columns: new[] { "Status", "LastFailedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_failure_Topic_Partition_Offset",
                table: "audit_event_failure",
                columns: new[] { "Topic", "Partition", "Offset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event_failure");
        }
    }
}
