using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Payments.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialPayments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "payments");

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            schema: "payments",
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
            schema: "payments",
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
            schema: "payments",
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

        migrationBuilder.CreateTable(
            name: "payment_transactions",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ProviderTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProviderOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RawWebhookPayload = table.Column<string>(type: "text", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_transactions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_ExpiresAtUtc",
            schema: "payments",
            table: "idempotency_records",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Key",
            schema: "payments",
            table: "idempotency_records",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc_NextRetryUtc",
            schema: "payments",
            table: "outbox_messages",
            columns: new[] { "ProcessedOnUtc", "NextRetryUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_BookingId",
            schema: "payments",
            table: "payment_transactions",
            column: "BookingId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_ProviderOrderId",
            schema: "payments",
            table: "payment_transactions",
            column: "ProviderOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_ProviderTransactionId",
            schema: "payments",
            table: "payment_transactions",
            column: "ProviderTransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "idempotency_records",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "payment_transactions",
            schema: "payments");
    }
}
