using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class UpdateIdentitySchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "user_sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "refresh_tokens",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "phone_change_requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HttpMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                RequestPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                ResponseContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ResponseBody = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "identity",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ReceivedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.EventId, x.HandlerName });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                NextRetryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            schema: "identity",
            table: "idempotency_records",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Key",
            schema: "identity",
            table: "idempotency_records",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc_NextRetryUtc",
            schema: "identity",
            table: "outbox_messages",
            columns: new[] { "ProcessedOnUtc", "NextRetryUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "user_sessions");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "refresh_tokens");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "identity",
            table: "phone_change_requests");
    }
}
