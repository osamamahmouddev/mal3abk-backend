using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Api.Infrastructure.Media.Migrations;

/// <inheritdoc />
public partial class AddMediaContentHash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentSha256",
            schema: "media",
            table: "assets",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContentSha256",
            schema: "media",
            table: "assets");
    }
}
