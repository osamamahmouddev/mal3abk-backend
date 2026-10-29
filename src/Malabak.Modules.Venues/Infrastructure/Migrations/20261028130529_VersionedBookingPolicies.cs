using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class VersionedBookingPolicies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_policies_VenueId",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "DepositPercentage",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "RefundPolicyType",
            schema: "venues",
            table: "policies");

        migrationBuilder.RenameColumn(
            name: "TermsAndConditions",
            schema: "venues",
            table: "policies",
            newName: "Terms");

        migrationBuilder.RenameColumn(
            name: "CancellationWindowHours",
            schema: "venues",
            table: "policies",
            newName: "Version");

        migrationBuilder.AddColumn<bool>(
            name: "AllowReschedule",
            schema: "venues",
            table: "policies",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "CancellationDeadlineHours",
            schema: "venues",
            table: "policies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<Guid>(
            name: "CreatedByUserId",
            schema: "venues",
            table: "policies",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<string>(
            name: "DepositType",
            schema: "venues",
            table: "policies",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "DepositValue",
            schema: "venues",
            table: "policies",
            type: "numeric(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<DateTime>(
            name: "EffectiveFromUtc",
            schema: "venues",
            table: "policies",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<bool>(
            name: "IsCurrent",
            schema: "venues",
            table: "policies",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "LateArrivalGraceMinutes",
            schema: "venues",
            table: "policies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "ManualReviewWindowMinutes",
            schema: "venues",
            table: "policies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "NoShowRule",
            schema: "venues",
            table: "policies",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "PendingBookingsBlockSlots",
            schema: "venues",
            table: "policies",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "ProofSubmissionWindowMinutes",
            schema: "venues",
            table: "policies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "RefundEligibility",
            schema: "venues",
            table: "policies",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "RefundPercentage",
            schema: "venues",
            table: "policies",
            type: "numeric(5,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "RescheduleDeadlineHours",
            schema: "venues",
            table: "policies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "SupersededAtUtc",
            schema: "venues",
            table: "policies",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_policies_VenueId_Version",
            schema: "venues",
            table: "policies",
            columns: new[] { "VenueId", "Version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_policies_single_current",
            schema: "venues",
            table: "policies",
            column: "VenueId",
            unique: true,
            filter: "\"IsCurrent\" = TRUE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_policies_VenueId_Version",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropIndex(
            name: "ux_policies_single_current",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "AllowReschedule",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "CancellationDeadlineHours",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "CreatedByUserId",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "DepositType",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "DepositValue",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "EffectiveFromUtc",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "IsCurrent",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "LateArrivalGraceMinutes",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "ManualReviewWindowMinutes",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "NoShowRule",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "PendingBookingsBlockSlots",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "ProofSubmissionWindowMinutes",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "RefundEligibility",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "RefundPercentage",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "RescheduleDeadlineHours",
            schema: "venues",
            table: "policies");

        migrationBuilder.DropColumn(
            name: "SupersededAtUtc",
            schema: "venues",
            table: "policies");

        migrationBuilder.RenameColumn(
            name: "Version",
            schema: "venues",
            table: "policies",
            newName: "CancellationWindowHours");

        migrationBuilder.RenameColumn(
            name: "Terms",
            schema: "venues",
            table: "policies",
            newName: "TermsAndConditions");

        migrationBuilder.AddColumn<decimal>(
            name: "DepositPercentage",
            schema: "venues",
            table: "policies",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "RefundPolicyType",
            schema: "venues",
            table: "policies",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_policies_VenueId",
            schema: "venues",
            table: "policies",
            column: "VenueId");
    }
}
