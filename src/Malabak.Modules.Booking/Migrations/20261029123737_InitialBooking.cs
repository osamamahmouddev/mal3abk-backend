using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class InitialBooking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "booking");

        migrationBuilder.CreateTable(
            name: "bookings",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingReference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PitchId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                total_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                total_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                deposit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                deposit_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                HoldExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RowVersion = table.Column<long>(type: "bigint", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_bookings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "booking_status_histories",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                FromStatus = table.Column<int>(type: "integer", nullable: false),
                ToStatus = table.Column<int>(type: "integer", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ChangedByUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_booking_status_histories", x => x.Id);
                table.ForeignKey(
                    name: "FK_booking_status_histories_bookings_BookingId",
                    column: x => x.BookingId,
                    principalSchema: "booking",
                    principalTable: "bookings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_booking_status_histories_BookingId",
            schema: "booking",
            table: "booking_status_histories",
            column: "BookingId");

        migrationBuilder.CreateIndex(
            name: "IX_bookings_BookingReference",
            schema: "booking",
            table: "bookings",
            column: "BookingReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_bookings_PitchId_StartUtc_EndUtc",
            schema: "booking",
            table: "bookings",
            columns: new[] { "PitchId", "StartUtc", "EndUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "booking_status_histories",
            schema: "booking");

        migrationBuilder.DropTable(
            name: "bookings",
            schema: "booking");
    }
}
