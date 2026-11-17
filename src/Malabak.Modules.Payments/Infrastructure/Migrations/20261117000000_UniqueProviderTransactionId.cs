using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Payments.Infrastructure.Migrations;

public partial class UniqueProviderTransactionId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_payment_transactions_ProviderTransactionId",
            schema: "payments",
            table: "payment_transactions",
            column: "ProviderTransactionId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payment_transactions_ProviderTransactionId",
            schema: "payments",
            table: "payment_transactions");
    }
}
