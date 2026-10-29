using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SpecialSchedules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "booking_rules",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: true),
                MinDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                MaxDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                IncrementMinutes = table.Column<int>(type: "integer", nullable: false),
                MinNoticeMinutes = table.Column<int>(type: "integer", nullable: false),
                MaxAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_booking_rules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "pitch_weekly_hours",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: false),
                DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                OpenTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                CloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pitch_weekly_hours", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "schedule_overrides",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: true),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                FromTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                ToTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_overrides", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_booking_rules_VenueId_PitchId",
            schema: "venues",
            table: "booking_rules",
            columns: new[] { "VenueId", "PitchId" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "IX_pitch_weekly_hours_PitchId_DayOfWeek",
            schema: "venues",
            table: "pitch_weekly_hours",
            columns: new[] { "PitchId", "DayOfWeek" },
            unique: true,
            filter: "\"IsDeleted\" = FALSE");

        migrationBuilder.CreateIndex(
            name: "IX_pitch_weekly_hours_VenueId",
            schema: "venues",
            table: "pitch_weekly_hours",
            column: "VenueId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_overrides_VenueId_StartDate_EndDate",
            schema: "venues",
            table: "schedule_overrides",
            columns: new[] { "VenueId", "StartDate", "EndDate" },
            filter: "\"IsDeleted\" = FALSE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "booking_rules",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "pitch_weekly_hours",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "schedule_overrides",
            schema: "venues");
    }
}
