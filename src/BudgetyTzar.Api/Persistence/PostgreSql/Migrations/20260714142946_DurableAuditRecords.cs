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
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_application_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_name = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_records", x => x.audit_record_id);
                    table.CheckConstraint("ck_audit_records_operation_name_not_blank", "length(btrim(operation_name)) > 0");
                    table.CheckConstraint("ck_audit_records_resource_id_not_empty", "resource_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_audit_records_resource_type_not_blank", "length(btrim(resource_type)) > 0");
                    table.ForeignKey(
                        name: "fk_audit_records_actor_application_user_id",
                        column: x => x.actor_application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_audit_records_application_users_application_user_id",
                        column: x => x.application_user_id,
                        principalSchema: "budgetytzar",
                        principalTable: "application_users",
                        principalColumn: "application_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_actor_application_user_id",
                schema: "budgetytzar",
                table: "audit_records",
                column: "actor_application_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_application_user_id_occurred_at_utc",
                schema: "budgetytzar",
                table: "audit_records",
                columns: new[] { "application_user_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_records_application_user_id_resource",
                schema: "budgetytzar",
                table: "audit_records",
                columns: new[] { "application_user_id", "resource_type", "resource_id" });
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
