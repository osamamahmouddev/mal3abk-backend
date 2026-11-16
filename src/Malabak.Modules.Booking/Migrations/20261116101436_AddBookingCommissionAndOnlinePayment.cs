using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddBookingCommissionAndOnlinePayment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCommissionPaid",
            schema: "booking",
            table: "bookings",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "PaymobOrderId",
            schema: "booking",
            table: "bookings",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PaymobTransactionId",
            schema: "booking",
            table: "bookings",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "online_paid_amount",
            schema: "booking",
            table: "bookings",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "online_paid_currency",
            schema: "booking",
            table: "bookings",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "platform_commission_amount",
            schema: "booking",
            table: "bookings",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "platform_commission_currency",
            schema: "booking",
            table: "bookings",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "venue_net_amount",
            schema: "booking",
            table: "bookings",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "venue_net_currency",
            schema: "booking",
            table: "bookings",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "venue_pay_amount",
            schema: "booking",
            table: "bookings",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "venue_pay_currency",
            schema: "booking",
            table: "bookings",
            type: "character varying(3)",
            maxLength: 3,
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsCommissionPaid",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "PaymobOrderId",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "PaymobTransactionId",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "online_paid_amount",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "online_paid_currency",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "platform_commission_amount",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "platform_commission_currency",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "venue_net_amount",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "venue_net_currency",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "venue_pay_amount",
            schema: "booking",
            table: "bookings");

        migrationBuilder.DropColumn(
            name: "venue_pay_currency",
            schema: "booking",
            table: "bookings");
    }
}
