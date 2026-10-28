using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialVenues : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "venues");

        migrationBuilder.CreateTable(
            name: "venues",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Latitude = table.Column<double>(type: "double precision", nullable: true),
                Longitude = table.Column<double>(type: "double precision", nullable: true),
                PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IsApproved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                IsPublished = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                Facilities = table.Column<string>(type: "text", nullable: false),
                Photos = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_venues", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "blocked_slots",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: false),
                StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                BlockedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_blocked_slots", x => x.Id);
                table.ForeignKey(
                    name: "FK_blocked_slots_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "operating_schedules",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                OpenTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                CloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                SlotDurationMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_operating_schedules", x => x.Id);
                table.ForeignKey(
                    name: "FK_operating_schedules_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "payment_methods",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                MethodType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AccountIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_methods", x => x.Id);
                table.ForeignKey(
                    name: "FK_payment_methods_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "pitches",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TurfType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Capacity = table.Column<int>(type: "integer", nullable: false),
                LengthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                WidthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                Photos = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pitches", x => x.Id);
                table.ForeignKey(
                    name: "FK_pitches_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "policies",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                DepositPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                CancellationWindowHours = table.Column<int>(type: "integer", nullable: false),
                RefundPolicyType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TermsAndConditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policies", x => x.Id);
                table.ForeignKey(
                    name: "FK_policies_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "price_rules",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: true),
                DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_price_rules", x => x.Id);
                table.ForeignKey(
                    name: "FK_price_rules_venues_VenueId",
                    column: x => x.VenueId,
                    principalSchema: "venues",
                    principalTable: "venues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_blocked_slots_PitchId_StartUtc_EndUtc",
            schema: "venues",
            table: "blocked_slots",
            columns: new[] { "PitchId", "StartUtc", "EndUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_blocked_slots_VenueId",
            schema: "venues",
            table: "blocked_slots",
            column: "VenueId");

        migrationBuilder.CreateIndex(
            name: "IX_operating_schedules_VenueId_DayOfWeek",
            schema: "venues",
            table: "operating_schedules",
            columns: new[] { "VenueId", "DayOfWeek" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_methods_VenueId_IsActive",
            schema: "venues",
            table: "payment_methods",
            columns: new[] { "VenueId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_pitches_VenueId_IsActive",
            schema: "venues",
            table: "pitches",
            columns: new[] { "VenueId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_policies_VenueId",
            schema: "venues",
            table: "policies",
            column: "VenueId");

        migrationBuilder.CreateIndex(
            name: "IX_price_rules_VenueId_DayOfWeek",
            schema: "venues",
            table: "price_rules",
            columns: new[] { "VenueId", "DayOfWeek" });

        migrationBuilder.CreateIndex(
            name: "IX_price_rules_VenueId_PitchId",
            schema: "venues",
            table: "price_rules",
            columns: new[] { "VenueId", "PitchId" });

        migrationBuilder.CreateIndex(
            name: "IX_venues_City_Area",
            schema: "venues",
            table: "venues",
            columns: new[] { "City", "Area" });

        migrationBuilder.CreateIndex(
            name: "IX_venues_IsApproved_IsPublished",
            schema: "venues",
            table: "venues",
            columns: new[] { "IsApproved", "IsPublished" });

        migrationBuilder.CreateIndex(
            name: "IX_venues_OwnerId",
            schema: "venues",
            table: "venues",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_venues_Slug",
            schema: "venues",
            table: "venues",
            column: "Slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "blocked_slots",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "operating_schedules",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "payment_methods",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "pitches",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "policies",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "price_rules",
            schema: "venues");

        migrationBuilder.DropTable(
            name: "venues",
            schema: "venues");
    }
}
