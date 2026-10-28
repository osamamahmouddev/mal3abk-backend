using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Api.Infrastructure.Media.Migrations;

/// <inheritdoc />
public partial class InitialMedia : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "media");

        migrationBuilder.CreateTable(
            name: "assets",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Visibility = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                UploadTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UploadExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Width = table.Column<int>(type: "integer", nullable: true),
                Height = table.Column<int>(type: "integer", nullable: true),
                SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                RejectionReason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_assets", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_assets_OwnerUserId_CreatedAtUtc",
            schema: "media",
            table: "assets",
            columns: new[] { "OwnerUserId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "assets",
            schema: "media");
    }
}
