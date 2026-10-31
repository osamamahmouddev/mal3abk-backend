using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class VenueCustomerNotes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "venue_customer_notes",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                TextCiphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_venue_customer_notes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_venue_customer_notes_VenueId_CustomerKey_CreatedAtUtc",
            schema: "booking",
            table: "venue_customer_notes",
            columns: new[] { "VenueId", "CustomerKey", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "venue_customer_notes",
            schema: "booking");
    }
}
