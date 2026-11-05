using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Teams.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialTeams : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "teams");

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                HttpMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                RequestPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                ResponseContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ResponseBody = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "teams",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ReceivedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.EventId, x.HandlerName });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                NextRetryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "teams",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LogoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CoverUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Governorate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CaptainId = table.Column<Guid>(type: "uuid", nullable: false),
                InviteCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                InviteCodeExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsRecruiting = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                IsOpenForChallenges = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                Rating = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1500.0),
                RatingDeviation = table.Column<double>(type: "double precision", nullable: false, defaultValue: 350.0),
                RatingVolatility = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.059999999999999998),
                MatchesPlayed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Wins = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Draws = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                Losses = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                GoalsFor = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                GoalsAgainst = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_teams", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "team_members",
            schema: "teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                JerseyNumber = table.Column<int>(type: "integer", nullable: true),
                Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_team_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_team_members_teams_TeamId",
                    column: x => x.TeamId,
                    principalSchema: "teams",
                    principalTable: "teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            schema: "teams",
            table: "idempotency_records",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Key",
            schema: "teams",
            table: "idempotency_records",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc_NextRetryUtc",
            schema: "teams",
            table: "outbox_messages",
            columns: new[] { "ProcessedOnUtc", "NextRetryUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_team_members_PlayerId",
            schema: "teams",
            table: "team_members",
            column: "PlayerId");

        migrationBuilder.CreateIndex(
            name: "IX_team_members_TeamId_PlayerId",
            schema: "teams",
            table: "team_members",
            columns: new[] { "TeamId", "PlayerId" });

        migrationBuilder.CreateIndex(
            name: "IX_teams_CaptainId",
            schema: "teams",
            table: "teams",
            column: "CaptainId");

        migrationBuilder.CreateIndex(
            name: "IX_teams_InviteCode",
            schema: "teams",
            table: "teams",
            column: "InviteCode");

        migrationBuilder.CreateIndex(
            name: "IX_teams_Slug",
            schema: "teams",
            table: "teams",
            column: "Slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "teams");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "teams");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "teams");

        migrationBuilder.DropTable(
            name: "team_members",
            schema: "teams");

        migrationBuilder.DropTable(
            name: "teams",
            schema: "teams");
    }
}
