using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Support.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SupportTickets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "support");

        migrationBuilder.CreateTable(
            name: "tickets",
            schema: "support",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                RequesterKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RelatedBookingId = table.Column<Guid>(type: "uuid", nullable: true),
                RelatedVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                RelatedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                ResolutionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tickets", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ticket_messages",
            schema: "support",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                IsInternal = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ticket_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_ticket_messages_tickets_TicketId",
                    column: x => x.TicketId,
                    principalSchema: "support",
                    principalTable: "tickets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ticket_messages_TicketId_CreatedAtUtc",
            schema: "support",
            table: "ticket_messages",
            columns: new[] { "TicketId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_AssigneeId",
            schema: "support",
            table: "tickets",
            column: "AssigneeId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_Number",
            schema: "support",
            table: "tickets",
            column: "Number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_RelatedBookingId",
            schema: "support",
            table: "tickets",
            column: "RelatedBookingId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_RelatedVenueId_Category",
            schema: "support",
            table: "tickets",
            columns: new[] { "RelatedVenueId", "Category" });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_RequesterId_CreatedAtUtc",
            schema: "support",
            table: "tickets",
            columns: new[] { "RequesterId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_tickets_Status_Priority_LastActivityAtUtc",
            schema: "support",
            table: "tickets",
            columns: new[] { "Status", "Priority", "LastActivityAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ticket_messages",
            schema: "support");

        migrationBuilder.DropTable(
            name: "tickets",
            schema: "support");
    }
}
