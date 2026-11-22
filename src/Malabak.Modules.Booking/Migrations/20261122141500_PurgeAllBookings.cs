using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <summary>
/// One-off cleanup requested by the platform owner to wipe all booking data in production/staging environments.
/// Clears all bookings, status histories, proofs, refunds, disputes, payouts, and customer notes,
/// and unlinks booking references in matches, challenges, support tickets, trash entries, and payment transactions.
/// </summary>
public partial class PurgeAllBookings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                -- 1. Unlink references in Matches module
                IF to_regclass('matches.matches') IS NOT NULL THEN
                    UPDATE matches.matches SET "BookingId" = NULL WHERE "BookingId" IS NOT NULL;
                END IF;

                IF to_regclass('matches.team_challenges') IS NOT NULL THEN
                    UPDATE matches.team_challenges SET "BookingId" = NULL WHERE "BookingId" IS NOT NULL;
                END IF;

                -- 2. Unlink references in Support module
                IF to_regclass('support.tickets') IS NOT NULL THEN
                    UPDATE support.tickets SET "RelatedBookingId" = NULL WHERE "RelatedBookingId" IS NOT NULL;
                END IF;

                -- 3. Delete trash entries for bookings
                IF to_regclass('platform.trash_entries') IS NOT NULL THEN
                    DELETE FROM platform.trash_entries WHERE "Kind" = 'booking';
                END IF;

                -- 4. Delete payment transactions related to bookings
                IF to_regclass('payments.payment_transactions') IS NOT NULL THEN
                    DELETE FROM payments.payment_transactions;
                END IF;

                -- 5. Delete child booking tables
                IF to_regclass('booking.dispute_evidence') IS NOT NULL THEN
                    DELETE FROM booking.dispute_evidence;
                END IF;

                IF to_regclass('booking.payment_disputes') IS NOT NULL THEN
                    DELETE FROM booking.payment_disputes;
                END IF;

                IF to_regclass('booking.booking_refunds') IS NOT NULL THEN
                    DELETE FROM booking.booking_refunds;
                END IF;

                IF to_regclass('booking.payment_proofs') IS NOT NULL THEN
                    DELETE FROM booking.payment_proofs;
                END IF;

                IF to_regclass('booking.booking_status_histories') IS NOT NULL THEN
                    DELETE FROM booking.booking_status_histories;
                END IF;

                IF to_regclass('booking.venue_payouts') IS NOT NULL THEN
                    DELETE FROM booking.venue_payouts;
                END IF;

                IF to_regclass('booking.venue_customer_notes') IS NOT NULL THEN
                    DELETE FROM booking.venue_customer_notes;
                END IF;

                -- 6. Delete all bookings
                IF to_regclass('booking.bookings') IS NOT NULL THEN
                    DELETE FROM booking.bookings;
                END IF;

                -- 7. Clean outbox/inbox messages for booking to prevent spurious events
                IF to_regclass('booking.outbox_messages') IS NOT NULL THEN
                    DELETE FROM booking.outbox_messages;
                END IF;

                IF to_regclass('booking.inbox_messages') IS NOT NULL THEN
                    DELETE FROM booking.inbox_messages;
                END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Purged data cannot be restored.
    }
}
