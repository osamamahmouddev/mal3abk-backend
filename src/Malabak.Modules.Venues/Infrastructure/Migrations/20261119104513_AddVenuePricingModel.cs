using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddVenuePricingModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PricingModel",
            schema: "venues",
            table: "venues",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Subscription");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PricingModel",
            schema: "venues",
            table: "venues");
    }
}
