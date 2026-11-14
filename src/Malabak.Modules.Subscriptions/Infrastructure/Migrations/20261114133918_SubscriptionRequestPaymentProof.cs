using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Subscriptions.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SubscriptionRequestPaymentProof : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPaymentReported",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "PaymentProofMediaId",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PaymentProofUrl",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PaymentTransferredAtUtc",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TransferNotes",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TransferReference",
            schema: "subscriptions",
            table: "subscription_requests",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsPaymentReported",
            schema: "subscriptions",
            table: "subscription_requests");

        migrationBuilder.DropColumn(
            name: "PaymentProofMediaId",
            schema: "subscriptions",
            table: "subscription_requests");

        migrationBuilder.DropColumn(
            name: "PaymentProofUrl",
            schema: "subscriptions",
            table: "subscription_requests");

        migrationBuilder.DropColumn(
            name: "PaymentTransferredAtUtc",
            schema: "subscriptions",
            table: "subscription_requests");

        migrationBuilder.DropColumn(
            name: "TransferNotes",
            schema: "subscriptions",
            table: "subscription_requests");

        migrationBuilder.DropColumn(
            name: "TransferReference",
            schema: "subscriptions",
            table: "subscription_requests");
    }
}
