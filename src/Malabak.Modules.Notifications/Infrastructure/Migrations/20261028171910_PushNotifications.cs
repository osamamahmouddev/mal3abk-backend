using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Notifications.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PushNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_tokens",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                Token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                Platform = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeactivatedReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_tokens", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "push_deliveries",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                DeviceTokenId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_push_deliveries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_device_tokens_SessionId",
            schema: "notifications",
            table: "device_tokens",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_device_tokens_Token",
            schema: "notifications",
            table: "device_tokens",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_device_tokens_UserId_IsActive",
            schema: "notifications",
            table: "device_tokens",
            columns: new[] { "UserId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_push_deliveries_NotificationId_DeviceTokenId",
            schema: "notifications",
            table: "push_deliveries",
            columns: new[] { "NotificationId", "DeviceTokenId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_push_deliveries_Status_NextAttemptAtUtc",
            schema: "notifications",
            table: "push_deliveries",
            columns: new[] { "Status", "NextAttemptAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "device_tokens",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "push_deliveries",
            schema: "notifications");
    }
}
