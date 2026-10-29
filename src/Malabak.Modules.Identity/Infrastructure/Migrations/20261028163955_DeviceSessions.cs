using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DeviceSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DeviceId",
            schema: "identity",
            table: "user_sessions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DeviceName",
            schema: "identity",
            table: "user_sessions",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsAdminSession",
            schema: "identity",
            table: "user_sessions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "RevokedReason",
            schema: "identity",
            table: "user_sessions",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_sessions_UserId_DeviceId",
            schema: "identity",
            table: "user_sessions",
            columns: new[] { "UserId", "DeviceId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_user_sessions_UserId_DeviceId",
            schema: "identity",
            table: "user_sessions");

        migrationBuilder.DropColumn(
            name: "DeviceId",
            schema: "identity",
            table: "user_sessions");

        migrationBuilder.DropColumn(
            name: "DeviceName",
            schema: "identity",
            table: "user_sessions");

        migrationBuilder.DropColumn(
            name: "IsAdminSession",
            schema: "identity",
            table: "user_sessions");

        migrationBuilder.DropColumn(
            name: "RevokedReason",
            schema: "identity",
            table: "user_sessions");
    }
}
