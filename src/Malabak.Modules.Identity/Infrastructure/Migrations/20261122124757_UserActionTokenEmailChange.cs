using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <summary>Email change codes: the address they move to and how many wrong codes were tried.</summary>
public partial class UserActionTokenEmailChange : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FailedAttempts",
            schema: "identity",
            table: "user_action_tokens",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "NewEmail",
            schema: "identity",
            table: "user_action_tokens",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FailedAttempts",
            schema: "identity",
            table: "user_action_tokens");

        migrationBuilder.DropColumn(
            name: "NewEmail",
            schema: "identity",
            table: "user_action_tokens");
    }
}
