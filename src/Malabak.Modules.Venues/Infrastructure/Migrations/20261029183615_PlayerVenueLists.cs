using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PlayerVenueLists : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "favorite_venues",
            schema: "venues",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_favorite_venues", x => new { x.UserId, x.VenueId });
            });

        migrationBuilder.CreateTable(
            name: "recently_viewed_venues",
            schema: "venues",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                ViewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_recently_viewed_venues", x => new { x.UserId, x.VenueId });
            });

        migrationBuilder.CreateIndex(
            name: "IX_favorite_venues_UserId_CreatedAtUtc",
            schema: "venues",
            table: "favorite_venues",
            columns: new[] { "UserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_recently_viewed_venues_UserId_ViewedAtUtc",
            schema: "venues",
            table: "recently_viewed_venues",
            columns: new[] { "UserId", "ViewedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "favorite_venues",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "recently_viewed_venues",
            schema: "venues");
    }
}
