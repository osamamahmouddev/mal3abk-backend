using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class EncryptPaymentMethodDetails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AccountIdentifier",
            schema: "venues",
            table: "payment_methods");

        migrationBuilder.RenameColumn(
            name: "AccountName",
            schema: "venues",
            table: "payment_methods",
            newName: "RecipientName");

        migrationBuilder.AddColumn<byte[]>(
            name: "AccountDetailsCiphertext",
            schema: "venues",
            table: "payment_methods",
            type: "bytea",
            nullable: false,
            defaultValue: new byte[0]);

        migrationBuilder.AddColumn<string>(
            name: "AccountDetailsMask",
            schema: "venues",
            table: "payment_methods",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "DepositType",
            schema: "venues",
            table: "payment_methods",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "DepositValue",
            schema: "venues",
            table: "payment_methods",
            type: "numeric(18,2)",
            nullable: false,
            defaultValue: 0m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AccountDetailsCiphertext",
            schema: "venues",
            table: "payment_methods");

        migrationBuilder.DropColumn(
            name: "AccountDetailsMask",
            schema: "venues",
            table: "payment_methods");

        migrationBuilder.DropColumn(
            name: "DepositType",
            schema: "venues",
            table: "payment_methods");

        migrationBuilder.DropColumn(
            name: "DepositValue",
            schema: "venues",
            table: "payment_methods");

        migrationBuilder.RenameColumn(
            name: "RecipientName",
            schema: "venues",
            table: "payment_methods",
            newName: "AccountName");

        migrationBuilder.AddColumn<string>(
            name: "AccountIdentifier",
            schema: "venues",
            table: "payment_methods",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");
    }
}
