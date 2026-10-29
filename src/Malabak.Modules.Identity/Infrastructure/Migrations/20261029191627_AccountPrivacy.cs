using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AccountPrivacy : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "AnonymizedAtUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletionRequestedAtUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "user_consents",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Granted = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_consents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_user_consents_UserId_Type_RecordedAtUtc",
            schema: "identity",
            table: "user_consents",
            columns: new[] { "UserId", "Type", "RecordedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_consents",
            schema: "identity");

        migrationBuilder.DropColumn(
            name: "AnonymizedAtUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "DeletionRequestedAtUtc",
            schema: "identity",
            table: "users");
    }
}
