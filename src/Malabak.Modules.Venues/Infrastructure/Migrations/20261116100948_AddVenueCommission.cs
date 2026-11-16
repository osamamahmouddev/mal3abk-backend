using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddVenueCommission : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "CommissionRate",
            schema: "venues",
            table: "venues",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CommissionType",
            schema: "venues",
            table: "venues",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "PlatformDefault");

        migrationBuilder.AddColumn<bool>(
            name: "CustomCommissionEnabled",
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
            name: "CommissionRate",
            schema: "venues",
            table: "venues");

        migrationBuilder.DropColumn(
            name: "CommissionType",
            schema: "venues",
            table: "venues");

        migrationBuilder.DropColumn(
            name: "CustomCommissionEnabled",
            schema: "venues",
            table: "venues");
    }
}
