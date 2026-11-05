using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Matches.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddMatchResultsToMatchesModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DisputeReason",
            schema: "matches",
            table: "matches",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DisputedAtUtc",
            schema: "matches",
            table: "matches",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ResultStatus",
            schema: "matches",
            table: "matches",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "ResultSubmittedAtUtc",
            schema: "matches",
            table: "matches",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ResultSubmittedByPlayerId",
            schema: "matches",
            table: "matches",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ResultSubmittedByTeamId",
            schema: "matches",
            table: "matches",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ResultVerifiedAtUtc",
            schema: "matches",
            table: "matches",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ResultVerifiedByPlayerId",
            schema: "matches",
            table: "matches",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ResultVerifiedByTeamId",
            schema: "matches",
            table: "matches",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_matches_ResultStatus",
            schema: "matches",
            table: "matches",
            column: "ResultStatus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_matches_ResultStatus",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "DisputeReason",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "DisputedAtUtc",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultStatus",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultSubmittedAtUtc",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultSubmittedByPlayerId",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultSubmittedByTeamId",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultVerifiedAtUtc",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultVerifiedByPlayerId",
            schema: "matches",
            table: "matches");

        migrationBuilder.DropColumn(
            name: "ResultVerifiedByTeamId",
            schema: "matches",
            table: "matches");
    }
}
