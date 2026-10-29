using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PasswordAuthentication : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FailedLoginCount",
            schema: "identity",
            table: "users",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "LoginThrottledUntilUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "TotpSecretCiphertext",
            schema: "identity",
            table: "users",
            type: "bytea",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "user_action_tokens",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_action_tokens", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_user_action_tokens_TokenHash",
            schema: "identity",
            table: "user_action_tokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_action_tokens_UserId_Purpose",
            schema: "identity",
            table: "user_action_tokens",
            columns: new[] { "UserId", "Purpose" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_action_tokens",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "FailedLoginCount",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "LoginThrottledUntilUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "TotpSecretCiphertext",
            schema: "identity",
            table: "users");
    }
}
