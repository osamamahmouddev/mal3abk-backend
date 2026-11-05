using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class BookingModuleFinalModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RowVersion",
            schema: "booking",
            table: "payment_proofs");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            schema: "booking",
            table: "booking_refunds");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "RowVersion",
            schema: "booking",
            table: "payment_proofs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "RowVersion",
            schema: "booking",
            table: "bookings",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "RowVersion",
            schema: "booking",
            table: "booking_refunds",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }
}
