using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Booking.Migrations;

/// <inheritdoc />
public partial class PaymentDisputes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "payment_disputes",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                OpenedBy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_disputes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "dispute_evidence",
            schema: "booking",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ProofId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dispute_evidence", x => x.Id);
                table.ForeignKey(
                    name: "FK_dispute_evidence_payment_disputes_DisputeId",
                    column: x => x.DisputeId,
                    principalSchema: "booking",
                    principalTable: "payment_disputes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dispute_evidence_DisputeId_CreatedAtUtc",
            schema: "booking",
            table: "dispute_evidence",
            columns: new[] { "DisputeId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_disputes_BookingId",
            schema: "booking",
            table: "payment_disputes",
            column: "BookingId",
            unique: true,
            filter: "\"Status\" <> 'Resolved'");

        migrationBuilder.CreateIndex(
            name: "IX_payment_disputes_Status_CreatedAtUtc",
            schema: "booking",
            table: "payment_disputes",
            columns: new[] { "Status", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payment_disputes_VenueId",
            schema: "booking",
            table: "payment_disputes",
            column: "VenueId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "dispute_evidence",
            schema: "booking");

        migrationBuilder.DropTable(
            name: "payment_disputes",
            schema: "booking");
    }
}
