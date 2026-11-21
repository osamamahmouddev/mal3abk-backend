using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <summary>
/// Approving a venue now publishes it. Venues approved before that stayed drafts nobody could find, so this publishes
/// them once, at deploy. It runs a single time: an owner who hides a venue later is never overridden.
/// </summary>
public partial class PublishApprovedVenuesOnce : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE venues.venues
            SET "IsPublished" = TRUE, "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
            WHERE "IsApproved" = TRUE AND "ApprovalStatus" = 'Approved' AND "IsPublished" = FALSE AND "IsDeleted" = FALSE;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data-only: which venues were drafts before is not recorded, so there is nothing to undo.
    }
}
