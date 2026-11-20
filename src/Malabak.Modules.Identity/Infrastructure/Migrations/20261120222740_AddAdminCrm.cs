using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddAdminCrm : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "crm_contacts",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                VenueName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                Area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                PhoneEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                PhoneBlindIndex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                LostReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                NextFollowUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                WaitlistEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                StageChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_contacts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "crm_notes",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_notes", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_notes_crm_contacts_ContactId",
                    column: x => x.ContactId,
                    principalSchema: "identity",
                    principalTable: "crm_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_crm_contacts_PhoneBlindIndex",
            schema: "identity",
            table: "crm_contacts",
            column: "PhoneBlindIndex",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_contacts_Stage_NextFollowUpAtUtc",
            schema: "identity",
            table: "crm_contacts",
            columns: new[] { "Stage", "NextFollowUpAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_crm_notes_ContactId_CreatedAtUtc",
            schema: "identity",
            table: "crm_notes",
            columns: new[] { "ContactId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "crm_notes",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "crm_contacts",
            schema: "identity");
    }
}
