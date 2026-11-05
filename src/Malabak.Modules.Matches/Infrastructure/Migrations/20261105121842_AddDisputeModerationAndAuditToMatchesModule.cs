using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Matches.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddDisputeModerationAndAuditToMatchesModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AdminResolutionAction",
            schema: "matches",
            table: "matches",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AdminResolutionNotes",
            schema: "matches",
            table: "matches",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AdminResolvedAtUtc",
            schema: "matches",
            table: "matches",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AdminResolvedByUserId",
            schema: "matches",
            table: "matches",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "match_result_audit_records",
            schema: "matches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousHomeScore = table.Column<int>(type: "integer", nullable: true),
                PreviousAwayScore = table.Column<int>(type: "integer", nullable: true),
                NewHomeScore = table.Column<int>(type: "integer", nullable: true),
                NewAwayScore = table.Column<int>(type: "integer", nullable: true),
                PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                NewStatus = table.Column<int>(type: "integer", nullable: false),
                ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ChangeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_match_result_audit_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_match_result_audit_records_CreatedAtUtc",
            schema: "matches",
            table: "match_result_audit_records",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_match_result_audit_records_MatchId",
            schema: "matches",
            table: "match_result_audit_records",
            column: "MatchId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "match_result_audit_records",
            schema: "matches");

        migrationBuilder.DropColumn(
            name: "AdminResolutionAction",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "AdminResolutionNotes",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "AdminResolvedAtUtc",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "AdminResolvedByUserId",
            schema: "matches",
            table: "matches");
    }
}
