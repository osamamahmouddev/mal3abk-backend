using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VenueAdminModeration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AdminNote",
            schema: "venues",
            table: "venues",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "PublishedBeforeSuspension",
            schema: "venues",
            table: "venues",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AdminNote",
            schema: "venues",
            table: "venues");

        migrationBuilder.DropColumn(
            name: "PublishedBeforeSuspension",
            schema: "venues",
            table: "venues");
    }
}
