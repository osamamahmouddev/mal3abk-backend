using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VenueCityAreaSearchIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Explore filters city and area case-insensitively (lower(...) = ANY(...)), which the plain (City, Area)
        // index cannot serve. Expression indexes are not expressible in the EF model, hence raw SQL.
        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS ix_venues_searchable_city_area_lower
                ON venues.venues (lower("City"), lower("Area"))
                WHERE "IsPublished" = TRUE AND "IsDeleted" = FALSE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS venues.ix_venues_searchable_city_area_lower;");
    }
}
