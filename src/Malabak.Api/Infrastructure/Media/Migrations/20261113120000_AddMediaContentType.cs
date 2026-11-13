using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Api.Infrastructure.Media.Migrations;

/// <inheritdoc />
public partial class AddMediaContentType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContentType",
            schema: "media",
            table: "assets",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "image/webp");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContentType",
            schema: "media",
            table: "assets");
    }
}
