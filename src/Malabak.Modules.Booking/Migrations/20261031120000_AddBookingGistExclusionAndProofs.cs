using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddBookingGistExclusionAndProofs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

        migrationBuilder.AddColumn<Guid>(
            name: "VenueId",
            schema: "booking",
            table: "bookings",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<DateTime>(
            name: "ConfirmedAtUtc",
            schema: "booking",
            table: "bookings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CancelledAtUtc",
            schema: "booking",
            table: "bookings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancellationReason",
            schema: "booking",
            table: "bookings",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RejectionReason",
            schema: "booking",
            table: "bookings",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsManualBooking",
            schema: "booking",
            table: "bookings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "CustomerName",
            schema: "booking",
            table: "bookings",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CustomerPhone",
            schema: "booking",
            table: "bookings",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PriceSnapshotJson",
            schema: "booking",
            table: "bookings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PolicySnapshotJson",
            schema: "booking",
            table: "bookings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MetadataJson",
            schema: "booking",
            table: "booking_status_histories",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "payment_proofs",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                FileKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UploadedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsScanClean = table.Column<bool>(type: "boolean", nullable: false),
                ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AntivirusResult = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                IsApproved = table.Column<bool>(type: "boolean", nullable: true),
                RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReviewedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_proofs", x => x.Id);
                table.ForeignKey(
                    name: "FK_payment_proofs_bookings_BookingId",
                    column: x => x.BookingId,
                    principalSchema: "booking",
                    principalTable: "bookings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "booking_refunds",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ExpectedRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ActualRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                PolicySnapshotApplied = table.Column<string>(type: "text", nullable: true),
                RecordedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_booking_refunds", x => x.Id);
                table.ForeignKey(
                    name: "FK_booking_refunds_bookings_BookingId",
                    column: x => x.BookingId,
                    principalSchema: "booking",
                    principalTable: "bookings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_bookings_VenueId_StartUtc",
            schema: "booking",
            table: "bookings",
            columns: new[] { "VenueId", "StartUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_bookings_PlayerId_Status",
            schema: "booking",
            table: "bookings",
            columns: new[] { "PlayerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_proofs_BookingId",
            schema: "booking",
            table: "payment_proofs",
            column: "BookingId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_proofs_Sha256Hash",
            schema: "booking",
            table: "payment_proofs",
            column: "Sha256Hash");

        migrationBuilder.CreateIndex(
            name: "IX_booking_refunds_BookingId",
            schema: "booking",
            table: "booking_refunds",
            column: "BookingId");

        // GiST Exclusion Constraint preventing overlapping bookings on the same pitch for active states
        migrationBuilder.Sql(
            """
            ALTER TABLE booking.bookings
            ADD CONSTRAINT exclude_overlapping_active_bookings
            EXCLUDE USING gist (
                "PitchId" WITH =,
                tstzrange("StartUtc", "EndUtc") WITH &&
            )
            WHERE ("Status" IN (2, 3, 4, 5) AND "IsDeleted" = false);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE booking.bookings DROP CONSTRAINT IF EXISTS exclude_overlapping_active_bookings;");

        migrationBuilder.DropTable(
            name: "booking_refunds",
            schema: "booking");

        migrationBuilder.DropTable(
            name: "payment_proofs",
            schema: "booking");

        migrationBuilder.DropIndex(
            name: "IX_bookings_VenueId_StartUtc",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropIndex(
            name: "IX_bookings_PlayerId_Status",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "VenueId",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "ConfirmedAtUtc",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "CancelledAtUtc",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "CancellationReason",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "RejectionReason",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "IsManualBooking",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "CustomerName",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "CustomerPhone",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "PriceSnapshotJson",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "PolicySnapshotJson",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "MetadataJson",
            schema: "booking",
            table: "booking_status_histories");
    }
}
