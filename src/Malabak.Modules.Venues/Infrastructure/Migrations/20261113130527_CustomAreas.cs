using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class CustomAreas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "custom_areas",
            schema: "venues",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GovernorateCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                NameAr = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                NameEn = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_custom_areas", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_custom_areas_GovernorateCode_NameAr",
            schema: "venues",
            table: "custom_areas",
            columns: new[] { "GovernorateCode", "NameAr" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "custom_areas",
            schema: "venues");
    }
}
