using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Subscriptions.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SubscriptionRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscription_requests",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Cycle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                AdminReply = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscription_requests", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_subscription_requests_OwnerId",
            schema: "subscriptions",
            table: "subscription_requests",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_subscription_requests_Status_CreatedAtUtc",
            schema: "subscriptions",
            table: "subscription_requests",
            columns: new[] { "Status", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "subscription_requests",
            schema: "subscriptions");
    }
}
