using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <summary>
/// Splits the CRM board by region. Every contact the team had before this (including those in the trash) was in Helwan,
/// so they are all placed in «حلوان».
/// </summary>
public partial class CrmContactRegion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Region",
            schema: "identity",
            table: "crm_contacts",
            type: "character varying(60)",
            maxLength: 60,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_contacts_Region",
            schema: "identity",
            table: "crm_contacts",
            column: "Region");

        migrationBuilder.Sql("""UPDATE identity.crm_contacts SET "Region" = 'حلوان' WHERE "Region" IS NULL;""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_crm_contacts_Region",
            schema: "identity",
            table: "crm_contacts");

        migrationBuilder.DropColumn(
            name: "Region",
            schema: "identity",
            table: "crm_contacts");
    }
}
