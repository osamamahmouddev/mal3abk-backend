using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddUniqueRefundPerBooking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_booking_refunds_BookingId",
            schema: "booking",
            table: "booking_refunds");

        migrationBuilder.CreateIndex(
            name: "IX_booking_refunds_BookingId",
            schema: "booking",
            table: "booking_refunds",
            column: "BookingId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_booking_refunds_BookingId",
            schema: "booking",
            table: "booking_refunds");

        migrationBuilder.CreateIndex(
            name: "IX_booking_refunds_BookingId",
            schema: "booking",
            table: "booking_refunds",
            column: "BookingId");
    }
}
