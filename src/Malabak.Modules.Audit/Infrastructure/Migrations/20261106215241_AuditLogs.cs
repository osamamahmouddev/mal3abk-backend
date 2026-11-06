using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Audit.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AuditLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.CreateTable(
            name: "audit_logs",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ImpersonatorId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                BeforeJson = table.Column<string>(type: "text", nullable: true),
                AfterJson = table.Column<string>(type: "text", nullable: true),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_Action_OccurredAtUtc",
            schema: "audit",
            table: "audit_logs",
            columns: new[] { "Action", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_ActorId_OccurredAtUtc",
            schema: "audit",
            table: "audit_logs",
            columns: new[] { "ActorId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_EntityType_EntityId_OccurredAtUtc",
            schema: "audit",
            table: "audit_logs",
            columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_OccurredAtUtc",
            schema: "audit",
            table: "audit_logs",
            column: "OccurredAtUtc");

        // Append-only: the database itself refuses to change or remove audit rows, whoever connects (TASK-062).
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION audit.reject_audit_log_change() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'audit.audit_logs is append-only (% refused)', TG_OP USING ERRCODE = 'insufficient_privilege';
            END;
            $$;

            CREATE TRIGGER audit_logs_append_only
                BEFORE UPDATE OR DELETE ON audit.audit_logs
                FOR EACH ROW EXECUTE FUNCTION audit.reject_audit_log_change();

            CREATE TRIGGER audit_logs_no_truncate
                BEFORE TRUNCATE ON audit.audit_logs
                FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_audit_log_change();

            REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_logs FROM PUBLIC;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS audit_logs_append_only ON audit.audit_logs;
            DROP TRIGGER IF EXISTS audit_logs_no_truncate ON audit.audit_logs;
            DROP FUNCTION IF EXISTS audit.reject_audit_log_change();
            """);

        migrationBuilder.DropTable(
            name: "audit_logs",
            schema: "audit");
    }
}
