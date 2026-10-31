using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddBookingNotesAndProofArchive : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ArchivedAtUtc",
            schema: "booking",
            table: "payment_proofs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsArchived",
            schema: "booking",
            table: "payment_proofs",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            schema: "booking",
            table: "bookings",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ArchivedAtUtc",
            schema: "booking",
            table: "payment_proofs");

        migrationBuilder.DropColumn(
            name: "IsArchived",
            schema: "booking",
            table: "payment_proofs");

        migrationBuilder.DropColumn(
            name: "Notes",
            schema: "booking",
            table: "bookings");
    }
}
