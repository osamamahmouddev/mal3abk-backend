using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ExploreSearchIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_venues_searchable_location",
            schema: "venues",
            table: "venues",
            columns: new[] { "Latitude", "Longitude" },
            filter: "\"IsPublished\" = TRUE AND \"IsDeleted\" = FALSE AND \"Latitude\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_favorite_venues_VenueId",
            schema: "venues",
            table: "favorite_venues",
            column: "VenueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_venues_searchable_location",
            schema: "venues",
            table: "venues");

        migrationBuilder.DropIndex(
            name: "IX_favorite_venues_VenueId",
            schema: "venues",
            table: "favorite_venues");
    }
}
