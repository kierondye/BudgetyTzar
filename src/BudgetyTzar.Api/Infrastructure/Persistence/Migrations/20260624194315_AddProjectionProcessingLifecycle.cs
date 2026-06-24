using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionProcessingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "processed_projection_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "processed_projection_event",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingInstanceId",
                table: "processed_projection_event",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingStartedAt",
                table: "processed_projection_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingUpdatedAt",
                table: "processed_projection_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "processed_projection_event",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.Sql("""
                UPDATE processed_projection_event
                SET "Status" = 'Completed',
                    "CompletedAt" = "ProcessedAt",
                    "ProcessingUpdatedAt" = "ProcessedAt"
                """);

            migrationBuilder.Sql("""
                INSERT INTO processed_projection_event ("EventId", "EventType", "BudgetId", "OccurredAt", "ProcessedAt", "Status")
                SELECT o."Id", o."EventType", o."BudgetId", o."CreatedAt", o."CreatedAt", 'Pending'
                FROM "OutboxMessages" o
                WHERE o."BudgetId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM processed_projection_event p
                      WHERE p."EventId" = o."Id")
                """);

            migrationBuilder.CreateIndex(
                name: "IX_processed_projection_event_ProcessingInstanceId",
                table: "processed_projection_event",
                column: "ProcessingInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_processed_projection_event_Status_ProcessingUpdatedAt",
                table: "processed_projection_event",
                columns: new[] { "Status", "ProcessingUpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_processed_projection_event_ProcessingInstanceId",
                table: "processed_projection_event");

            migrationBuilder.DropIndex(
                name: "IX_processed_projection_event_Status_ProcessingUpdatedAt",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "ProcessingInstanceId",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "ProcessingUpdatedAt",
                table: "processed_projection_event");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "processed_projection_event");
        }
    }
}
