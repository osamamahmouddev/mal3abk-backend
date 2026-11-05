using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Matches.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddBookingLinkToChallenges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BookingId",
            schema: "matches",
            table: "team_challenges",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_team_challenges_BookingId",
            schema: "matches",
            table: "team_challenges",
            column: "BookingId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_team_challenges_BookingId",
            schema: "matches",
            table: "team_challenges");

        migrationBuilder.DropColumn(
            name: "BookingId",
            schema: "matches",
            table: "team_challenges");
    }
}
