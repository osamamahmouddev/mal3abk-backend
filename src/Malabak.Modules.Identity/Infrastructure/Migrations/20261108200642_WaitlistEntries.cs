using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class WaitlistEntries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "waitlist_entries",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ChosenPlan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                PhoneEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                PhoneBlindIndex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ConsentRecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_waitlist_entries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_waitlist_entries_Audience",
            schema: "identity",
            table: "waitlist_entries",
            column: "Audience");

        migrationBuilder.CreateIndex(
            name: "IX_waitlist_entries_CreatedAtUtc",
            schema: "identity",
            table: "waitlist_entries",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_waitlist_entries_PhoneBlindIndex",
            schema: "identity",
            table: "waitlist_entries",
            column: "PhoneBlindIndex",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "waitlist_entries",
            schema: "identity");
    }
}
