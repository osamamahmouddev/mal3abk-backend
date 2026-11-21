using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <summary>
/// One-off cleanup asked for by the platform owner: bookings of venues that no longer exist (deleted, or missing) showed up in
/// the reports as «ملعب غير معروف» with refunds still pending. They are removed with everything that hangs off them: status
/// history, receipts, refunds, disputes, payouts and the venue's customer notes. Venues still in the admin trash are kept
/// (they can be restored within 30 days). The audit log is append-only and is not touched.
/// Guarded so it runs on databases where the venues or platform schemas do not exist yet (fresh test databases).
/// </summary>
public partial class PurgeBookingsOfDeletedVenues : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                trash_exists boolean := to_regclass('platform.trash_entries') IS NOT NULL;
            BEGIN
                IF to_regclass('venues.venues') IS NULL THEN
                    RETURN;
                END IF;

                CREATE TEMP TABLE gone_venues ON COMMIT DROP AS
                SELECT DISTINCT b."VenueId" AS id
                FROM booking.bookings b
                WHERE NOT EXISTS (SELECT 1 FROM venues.venues v WHERE v."Id" = b."VenueId" AND v."IsDeleted" = false);

                IF trash_exists THEN
                    DELETE FROM gone_venues g
                    WHERE EXISTS (SELECT 1 FROM platform.trash_entries t WHERE t."Kind" = 'venue' AND t."EntityId" = g.id);
                END IF;

                CREATE TEMP TABLE gone_bookings ON COMMIT DROP AS
                SELECT b."Id" AS id FROM booking.bookings b WHERE b."VenueId" IN (SELECT id FROM gone_venues);

                DELETE FROM booking.dispute_evidence e
                WHERE e."DisputeId" IN (SELECT d."Id" FROM booking.payment_disputes d WHERE d."BookingId" IN (SELECT id FROM gone_bookings));
                DELETE FROM booking.payment_disputes WHERE "BookingId" IN (SELECT id FROM gone_bookings);
                DELETE FROM booking.booking_refunds WHERE "BookingId" IN (SELECT id FROM gone_bookings);
                DELETE FROM booking.payment_proofs WHERE "BookingId" IN (SELECT id FROM gone_bookings);
                DELETE FROM booking.booking_status_histories WHERE "BookingId" IN (SELECT id FROM gone_bookings);
                DELETE FROM booking.venue_payouts WHERE "VenueId" IN (SELECT id FROM gone_venues);
                DELETE FROM booking.venue_customer_notes WHERE "VenueId" IN (SELECT id FROM gone_venues);

                IF to_regclass('payments.payment_transactions') IS NOT NULL THEN
                    DELETE FROM payments.payment_transactions WHERE "BookingId" IN (SELECT id FROM gone_bookings);
                END IF;

                DELETE FROM booking.bookings WHERE "Id" IN (SELECT id FROM gone_bookings);
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted data cannot be brought back.
    }
}
