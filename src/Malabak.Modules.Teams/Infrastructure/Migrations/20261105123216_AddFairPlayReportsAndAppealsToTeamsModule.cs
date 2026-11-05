using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Teams.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddFairPlayReportsAndAppealsToTeamsModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "fair_play_reports",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReporterTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                ReporterPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                ReportedTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                Category = table.Column<int>(type: "integer", nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                EvidenceUrls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                PenaltyType = table.Column<int>(type: "integer", nullable: false),
                PointsDeducted = table.Column<int>(type: "integer", nullable: true),
                AdminDecisionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                AdminReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AdminReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AppealReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                AppealedByPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                AppealedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AdminAppealNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                AdminAppealResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AdminAppealResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fair_play_reports", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_fair_play_reports_CreatedAtUtc",
            schema: "teams",
            table: "fair_play_reports",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_fair_play_reports_ReportedTeamId",
            schema: "teams",
            table: "fair_play_reports",
            column: "ReportedTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_fair_play_reports_ReporterTeamId",
            schema: "teams",
            table: "fair_play_reports",
            column: "ReporterTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_fair_play_reports_Status",
            schema: "teams",
            table: "fair_play_reports",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "fair_play_reports",
            schema: "teams");
    }
}
