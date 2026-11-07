using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Subscriptions.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SubscriptionsAndInvoices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "subscriptions");

        migrationBuilder.CreateSequence(
            name: "invoice_number_seq",
            schema: "subscriptions");

        migrationBuilder.CreateTable(
            name: "invoices",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                PlanName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Cycle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                VoidedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "owner_subscriptions",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                Cycle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CurrentPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                GraceEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_owner_subscriptions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payments",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "plans",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                MonthlyPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                AnnualPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                MaxVenues = table.Column<int>(type: "integer", nullable: true),
                MaxPitches = table.Column<int>(type: "integer", nullable: true),
                MaxStaffPerVenue = table.Column<int>(type: "integer", nullable: true),
                Features = table.Column<string>(type: "text", nullable: false),
                TrialDays = table.Column<int>(type: "integer", nullable: false),
                GraceDays = table.Column<int>(type: "integer", nullable: false),
                IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_plans", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subscription_events",
            schema: "subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                FromPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                ToPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscription_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_subscription_events_owner_subscriptions_SubscriptionId",
                    column: x => x.SubscriptionId,
                    principalSchema: "subscriptions",
                    principalTable: "owner_subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_invoices_Number",
            schema: "subscriptions",
            table: "invoices",
            column: "Number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_invoices_OwnerId_IssuedAtUtc",
            schema: "subscriptions",
            table: "invoices",
            columns: new[] { "OwnerId", "IssuedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_owner_subscriptions_OwnerId",
            schema: "subscriptions",
            table: "owner_subscriptions",
            column: "OwnerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_owner_subscriptions_Status_CurrentPeriodEndUtc",
            schema: "subscriptions",
            table: "owner_subscriptions",
            columns: new[] { "Status", "CurrentPeriodEndUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payments_OwnerId_PaidAtUtc",
            schema: "subscriptions",
            table: "payments",
            columns: new[] { "OwnerId", "PaidAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payments_PaidAtUtc",
            schema: "subscriptions",
            table: "payments",
            column: "PaidAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_plans_Code",
            schema: "subscriptions",
            table: "plans",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_subscription_events_SubscriptionId_OccurredAtUtc",
            schema: "subscriptions",
            table: "subscription_events",
            columns: new[] { "SubscriptionId", "OccurredAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invoices",
            schema: "subscriptions");

        migrationBuilder.DropTable(
            name: "payments",
            schema: "subscriptions");

        migrationBuilder.DropTable(
            name: "plans",
            schema: "subscriptions");

        migrationBuilder.DropTable(
            name: "subscription_events",
            schema: "subscriptions");

        migrationBuilder.DropTable(
            name: "owner_subscriptions",
            schema: "subscriptions");

        migrationBuilder.DropSequence(
            name: "invoice_number_seq",
            schema: "subscriptions");
    }
}
