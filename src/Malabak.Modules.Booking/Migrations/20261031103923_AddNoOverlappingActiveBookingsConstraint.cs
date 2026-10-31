using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddNoOverlappingActiveBookingsConstraint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Double-booking protection lives in the database, not only in code (rule.md section 5): two active
        // bookings of one pitch can never overlap, whatever the application does or however requests race.
        // "Active" = the statuses that hold the slot, stored as integers (BookingStatus): PendingDeposit = 2,
        // ProofSubmitted = 3, UnderReview = 4, Confirmed = 5. A unit test pins these values. The range is
        // half-open '[)', so back-to-back bookings (one ends when the next starts) are allowed.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql("""
            ALTER TABLE booking.bookings
                ADD CONSTRAINT no_overlapping_active_bookings
                EXCLUDE USING gist ("PitchId" WITH =, tstzrange("StartUtc", "EndUtc", '[)') WITH &&)
                WHERE ("Status" IN (2, 3, 4, 5));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE booking.bookings DROP CONSTRAINT no_overlapping_active_bookings;");
    }
}
