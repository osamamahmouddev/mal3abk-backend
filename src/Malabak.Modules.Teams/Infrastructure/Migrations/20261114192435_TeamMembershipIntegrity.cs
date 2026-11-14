using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Teams.Infrastructure.Migrations;

/// <inheritdoc />
public partial class TeamMembershipIntegrity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_teams_InviteCode",
            schema: "teams",
            table: "teams");

        migrationBuilder.AddColumn<bool>(
            name: "RejoinBlocked",
            schema: "teams",
            table: "team_members",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Codes were only checked before insert, so a duplicate is possible in existing data. Every team but the
        // oldest holder of a shared code gets a fresh one, so the unique index below can be created.
        migrationBuilder.Sql("""
            UPDATE teams.teams AS t
            SET "InviteCode" = upper(substr(md5(random()::text || t."Id"::text), 1, 6))
            FROM (
                SELECT "Id", row_number() OVER (PARTITION BY "InviteCode" ORDER BY "CreatedAtUtc", "Id") AS rn
                FROM teams.teams
            ) AS d
            WHERE d."Id" = t."Id" AND d.rn > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_teams_InviteCode",
            schema: "teams",
            table: "teams",
            column: "InviteCode",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_teams_InviteCode",
            schema: "teams",
            table: "teams");

        migrationBuilder.DropColumn(
            name: "RejoinBlocked",
            schema: "teams",
            table: "team_members");

        migrationBuilder.CreateIndex(
            name: "IX_teams_InviteCode",
            schema: "teams",
            table: "teams",
            column: "InviteCode");
    }
}
