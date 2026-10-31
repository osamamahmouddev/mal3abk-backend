using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PricingRulesModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_price_rules_VenueId_DayOfWeek",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "DayOfWeek",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.AddColumn<int>(
            name: "MinDurationMinutes",
            schema: "venues",
            table: "price_rules",
            type: "integer",
            nullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "price_amount",
            schema: "venues",
            table: "price_rules",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2);

        migrationBuilder.AddColumn<string>(
            name: "Adjustment",
            schema: "venues",
            table: "price_rules",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "FixedHourlyRate");

        migrationBuilder.AddColumn<int>(
            name: "Days",
            schema: "venues",
            table: "price_rules",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "EndDate",
            schema: "venues",
            table: "price_rules",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            schema: "venues",
            table: "price_rules",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Base");

        migrationBuilder.AddColumn<string>(
            name: "Name",
            schema: "venues",
            table: "price_rules",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "Legacy price");

        migrationBuilder.AddColumn<decimal>(
            name: "Percent",
            schema: "venues",
            table: "price_rules",
            type: "numeric(9,2)",
            precision: 9,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "StartDate",
            schema: "venues",
            table: "price_rules",
            type: "date",
            nullable: true);

        // Rules created under the old flat model (one price per day and time) have no safe mapping to the new kinds,
        // keep them for reference but inactive so venue owners recreate them through the price rules API.
        migrationBuilder.Sql("UPDATE venues.price_rules SET \"IsActive\" = FALSE;");

        migrationBuilder.CreateIndex(
            name: "IX_price_rules_VenueId_Kind",
            schema: "venues",
            table: "price_rules",
            columns: new[] { "VenueId", "Kind" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_price_rules_VenueId_Kind",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "Adjustment",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "Days",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "EndDate",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "Kind",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "Name",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "Percent",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "StartDate",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.DropColumn(
            name: "MinDurationMinutes",
            schema: "venues",
            table: "price_rules");

        migrationBuilder.AddColumn<int>(
            name: "DayOfWeek",
            schema: "venues",
            table: "price_rules",
            type: "integer",
            nullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "price_amount",
            schema: "venues",
            table: "price_rules",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,2)",
            oldPrecision: 18,
            oldScale: 2,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_price_rules_VenueId_DayOfWeek",
            schema: "venues",
            table: "price_rules",
            columns: new[] { "VenueId", "DayOfWeek" });
    }
}
