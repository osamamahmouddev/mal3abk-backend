using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AdminAuthentication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsAdminSession",
            schema: "identity",
            table: "refresh_tokens",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "admin_recovery_codes",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_admin_recovery_codes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_admin_recovery_codes_UserId_CodeHash",
            schema: "identity",
            table: "admin_recovery_codes",
            columns: new[] { "UserId", "CodeHash" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "admin_recovery_codes",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "IsAdminSession",
            schema: "identity",
            table: "refresh_tokens");
    }
}
