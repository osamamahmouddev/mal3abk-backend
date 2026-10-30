using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <summary>
/// Entity&lt;TId&gt; gained DeletedAtUtc on dev (UpdateVenuesSchema adds it to the tables that existed there). This adds
/// it to the Phase 1 tables that dev's migration could not know about. Generated with dotnet ef, then trimmed to the
/// columns UpdateVenuesSchema does not already add.
/// </summary>
public partial class SoftDeleteColumnsFromBaseEntity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "staff_members",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "staff_invitations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "schedule_overrides",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "pitch_weekly_hours",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "booking_rules",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "staff_members");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "staff_invitations");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "schedule_overrides");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "pitch_weekly_hours");

        migrationBuilder.DropColumn(
            name: "DeletedAtUtc",
            schema: "venues",
            table: "booking_rules");
    }
}
