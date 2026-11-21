using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Api.Infrastructure.Platform.Migrations;

/// <inheritdoc />
public partial class AdminTrash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "trash_entries",
            schema: "platform",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Subtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_trash_entries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_trash_entries_DeletedAtUtc",
            schema: "platform",
            table: "trash_entries",
            column: "DeletedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_trash_entries_Kind_EntityId",
            schema: "platform",
            table: "trash_entries",
            columns: new[] { "Kind", "EntityId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "trash_entries",
            schema: "platform");
    }
}
