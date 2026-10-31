using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class AddUniqueProofReceiptHash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payment_proofs_Sha256Hash",
            schema: "booking",
            table: "payment_proofs");

        migrationBuilder.CreateIndex(
            name: "IX_payment_proofs_Sha256Hash",
            schema: "booking",
            table: "payment_proofs",
            column: "Sha256Hash",
            unique: true,
            filter: "\"Sha256Hash\" <> '' AND \"IsApproved\" IS DISTINCT FROM FALSE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payment_proofs_Sha256Hash",
            schema: "booking",
            table: "payment_proofs");

        migrationBuilder.CreateIndex(
            name: "IX_payment_proofs_Sha256Hash",
            schema: "booking",
            table: "payment_proofs",
            column: "Sha256Hash");
    }
}
