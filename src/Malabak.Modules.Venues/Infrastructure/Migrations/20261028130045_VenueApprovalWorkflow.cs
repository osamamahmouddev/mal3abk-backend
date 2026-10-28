using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VenueApprovalWorkflow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ApprovalStatus",
            schema: "venues",
            table: "venues",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Draft");

        migrationBuilder.CreateTable(
            name: "venue_approval_events",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                FromStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ToStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_venue_approval_events", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_venues_ApprovalStatus",
            schema: "venues",
            table: "venues",
            column: "ApprovalStatus");

        migrationBuilder.CreateIndex(
            name: "IX_venue_approval_events_VenueId_CreatedAtUtc",
            schema: "venues",
            table: "venue_approval_events",
            columns: new[] { "VenueId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "venue_approval_events",
            schema: "venues");

        migrationBuilder.DropIndex(
            name: "IX_venues_ApprovalStatus",
            schema: "venues",
            table: "venues");

        migrationBuilder.DropColumn(
            name: "ApprovalStatus",
            schema: "venues",
            table: "venues");
    }
}
