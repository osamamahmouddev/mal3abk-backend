using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Matches.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddMatchesToMatchesModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "matches",
            schema: "matches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                HomeTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                AwayTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: true),
                VenueId = table.Column<Guid>(type: "uuid", nullable: true),
                VenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ScheduledStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ScheduledEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DurationMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Outcome = table.Column<int>(type: "integer", nullable: false),
                HomeScore = table.Column<int>(type: "integer", nullable: true),
                AwayScore = table.Column<int>(type: "integer", nullable: true),
                CancelledByTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_matches", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_matches_AwayTeamId",
            schema: "matches",
            table: "matches",
            column: "AwayTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_matches_BookingId",
            schema: "matches",
            table: "matches",
            column: "BookingId");

        migrationBuilder.CreateIndex(
            name: "IX_matches_ChallengeId",
            schema: "matches",
            table: "matches",
            column: "ChallengeId");

        migrationBuilder.CreateIndex(
            name: "IX_matches_HomeTeamId",
            schema: "matches",
            table: "matches",
            column: "HomeTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_matches_ScheduledStartUtc",
            schema: "matches",
            table: "matches",
            column: "ScheduledStartUtc");

        migrationBuilder.CreateIndex(
            name: "IX_matches_Status_ScheduledStartUtc_ScheduledEndUtc",
            schema: "matches",
            table: "matches",
            columns: new[] { "Status", "ScheduledStartUtc", "ScheduledEndUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "matches",
            schema: "matches");
    }
}
