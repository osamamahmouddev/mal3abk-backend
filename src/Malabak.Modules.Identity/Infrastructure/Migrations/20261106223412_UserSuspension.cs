using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class UserSuspension : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "SuspendedAtUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SuspensionReason",
            schema: "identity",
            table: "users",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_SuspendedAtUtc",
            schema: "identity",
            table: "users",
            column: "SuspendedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_users_SuspendedAtUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "SuspendedAtUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "SuspensionReason",
            schema: "identity",
            table: "users");
    }
}
