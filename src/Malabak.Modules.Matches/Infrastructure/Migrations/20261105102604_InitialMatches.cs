using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Matches.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialMatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "matches");

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "matches",
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
            schema: "matches",
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
            schema: "matches",
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
            name: "team_challenges",
            schema: "matches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ChallengerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                ChallengedTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                ChallengerCaptainId = table.Column<Guid>(type: "uuid", nullable: false),
                ProposedDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProposedDurationMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                ProposedPitchId = table.Column<Guid>(type: "uuid", nullable: true),
                ProposedVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                ProposedVenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CurrentTurnTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeclinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeclinedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_team_challenges", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            schema: "matches",
            table: "idempotency_records",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Key",
            schema: "matches",
            table: "idempotency_records",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc_NextRetryUtc",
            schema: "matches",
            table: "outbox_messages",
            columns: new[] { "ProcessedOnUtc", "NextRetryUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_team_challenges_ChallengedTeamId_Status",
            schema: "matches",
            table: "team_challenges",
            columns: new[] { "ChallengedTeamId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_team_challenges_ChallengerTeamId_Status",
            schema: "matches",
            table: "team_challenges",
            columns: new[] { "ChallengerTeamId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_team_challenges_ProposedDateUtc",
            schema: "matches",
            table: "team_challenges",
            column: "ProposedDateUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "matches");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "matches");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "matches");

        migrationBuilder.DropTable(
            name: "team_challenges",
            schema: "matches");
    }
}
