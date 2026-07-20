using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetyTzar.Api.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class DurableAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "budgetytzar",
                columns: table => new
                {
                    audit_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persisted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_name = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_records", x => x.audit_record_id);
                    table.CheckConstraint("ck_audit_records_action_not_blank", "length(btrim(action)) > 0");
                    table.CheckConstraint("ck_audit_records_correlation_id_not_blank", "length(btrim(correlation_id)) > 0");
                    table.CheckConstraint("ck_audit_records_operation_name_not_blank", "length(btrim(operation_name)) > 0");
                    table.ForeignKey(
                        name: "fk_audit_records_application_users_application_user_id",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_application_user_id_action",
                schema: "budgetytzar",
                table: "audit_records",
                columns: new[] { "application_user_id", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_application_user_id_persisted_at_utc",
                schema: "budgetytzar",
                table: "audit_records",
                columns: new[] { "application_user_id", "persisted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_correlation_id",
                schema: "budgetytzar",
                table: "audit_records",
                column: "correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "budgetytzar");
        }
    }
}
