using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionReadinessAllocationNotesAndOutboxLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "TransactionAllocations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishingLockId",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishingLockedAt",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishingLockId",
                table: "OutboxMessages",
                column: "PublishingLockId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_PublishingLockedAt_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "PublishingLockedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishingLockId",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_PublishingLockedAt_CreatedAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "TransactionAllocations");

            migrationBuilder.DropColumn(
                name: "PublishingLockId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "PublishingLockedAt",
                table: "OutboxMessages");
        }
    }
}
