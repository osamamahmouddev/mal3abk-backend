using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Notifications.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "notifications");

        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DataJson = table.Column<string>(type: "text", nullable: false),
                DeepLink = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                DataJson = table.Column<string>(type: "text", nullable: false),
                DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "preferences",
            schema: "notifications",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                InAppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                PushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_preferences", x => new { x.UserId, x.Category });
            });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_UserId_CreatedAtUtc",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "UserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_UserId_DedupeKey",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "UserId", "DedupeKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_notifications_UserId_ReadAtUtc",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "UserId", "ReadAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_ProcessedAtUtc_CreatedAtUtc",
            schema: "notifications",
            table: "outbox",
            columns: new[] { "ProcessedAtUtc", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notifications",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "outbox",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "preferences",
            schema: "notifications");
    }
}
