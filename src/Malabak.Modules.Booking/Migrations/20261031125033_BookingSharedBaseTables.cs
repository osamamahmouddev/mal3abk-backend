using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class BookingSharedBaseTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Optimistic concurrency now uses PostgreSQL's built-in xmin system column, like every other module
        // (BaseDbContext). A system column cannot be created or renamed into, so the old RowVersion columns, which
        // nothing ever updated, are dropped; xmin already exists on every table.
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

        migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "booking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "booking",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ReceivedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.EventId, x.HandlerName });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                NextRetryUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            schema: "booking",
            table: "idempotency_records",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Key",
            schema: "booking",
            table: "idempotency_records",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc_NextRetryUtc",
            schema: "booking",
            table: "outbox_messages",
            columns: new[] { "ProcessedOnUtc", "NextRetryUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "booking");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "booking");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "booking");

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
