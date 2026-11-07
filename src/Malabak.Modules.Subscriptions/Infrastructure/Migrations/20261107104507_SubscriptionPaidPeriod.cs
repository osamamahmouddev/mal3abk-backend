using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Subscriptions.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SubscriptionPaidPeriod : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CurrentPeriodPaid",
            schema: "subscriptions",
            table: "owner_subscriptions",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "CurrentPeriodStartUtc",
            schema: "subscriptions",
            table: "owner_subscriptions",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        // Existing subscriptions: the running period started when the subscription did.
        migrationBuilder.Sql("UPDATE subscriptions.owner_subscriptions SET \"CurrentPeriodStartUtc\" = \"StartedAtUtc\";");

        // Subscriptions that already recorded a payment have a paid period; the next payment must extend it, not repay it.
        migrationBuilder.Sql(
            "UPDATE subscriptions.owner_subscriptions s SET \"CurrentPeriodPaid\" = TRUE " +
            "WHERE EXISTS (SELECT 1 FROM subscriptions.subscription_events e WHERE e.\"SubscriptionId\" = s.\"Id\" AND e.\"Type\" = 'PaymentRecorded');");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CurrentPeriodPaid",
            schema: "subscriptions",
            table: "owner_subscriptions");

        migrationBuilder.DropColumn(
            name: "CurrentPeriodStartUtc",
            schema: "subscriptions",
            table: "owner_subscriptions");
    }
}
