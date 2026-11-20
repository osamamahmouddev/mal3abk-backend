using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AllVenuesOnCommission : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PricingModel",
            schema: "venues",
            table: "venues",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Commission",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldDefaultValue: "Subscription");

        // Subscriptions are retired: every existing venue moves to the per-booking commission model.
        migrationBuilder.Sql("UPDATE venues.venues SET \"PricingModel\" = 'Commission' WHERE \"PricingModel\" <> 'Commission';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PricingModel",
            schema: "venues",
            table: "venues",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Subscription",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldDefaultValue: "Commission");
    }
}
