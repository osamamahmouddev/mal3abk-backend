using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Teams.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddTeamJoinRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPublic",
            schema: "teams",
            table: "teams",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "team_join_requests",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReviewedByPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_team_join_requests", x => x.Id);
                table.ForeignKey(
                    name: "FK_team_join_requests_teams_TeamId",
                    column: x => x.TeamId,
                    principalSchema: "teams",
                    principalTable: "teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_team_join_requests_PlayerId",
            schema: "teams",
            table: "team_join_requests",
            column: "PlayerId");

        migrationBuilder.CreateIndex(
            name: "IX_team_join_requests_TeamId_PlayerId_Status",
            schema: "teams",
            table: "team_join_requests",
            columns: new[] { "TeamId", "PlayerId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "team_join_requests",
            schema: "teams");

        migrationBuilder.DropColumn(
            name: "IsPublic",
            schema: "teams",
            table: "teams");
    }
}
