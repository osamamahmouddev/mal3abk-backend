using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class FeaturedPlacements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "featured_placements",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                Slot = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_featured_placements", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_featured_placements_Slot_StartsAtUtc_EndsAtUtc",
            schema: "venues",
            table: "featured_placements",
            columns: new[] { "Slot", "StartsAtUtc", "EndsAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_featured_placements_VenueId",
            schema: "venues",
            table: "featured_placements",
            column: "VenueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "featured_placements",
            schema: "venues");
    }
}
