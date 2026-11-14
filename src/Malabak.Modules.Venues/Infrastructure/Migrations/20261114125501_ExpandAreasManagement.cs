using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Venues.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ExpandAreasManagement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "AddedByUserId",
            schema: "venues",
            table: "custom_areas",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<string>(
            name: "Code",
            schema: "venues",
            table: "custom_areas",
            type: "character varying(60)",
            maxLength: 60,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "DisplayOrder",
            schema: "venues",
            table: "custom_areas",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            schema: "venues",
            table: "custom_areas",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsCustom",
            schema: "venues",
            table: "custom_areas",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsFocus",
            schema: "venues",
            table: "custom_areas",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("UPDATE venues.custom_areas SET \"Code\" = 'helwan' WHERE \"NameAr\" = 'حلوان' AND (\"Code\" IS NULL OR \"Code\" = '');");
        migrationBuilder.Sql("UPDATE venues.custom_areas SET \"Code\" = SUBSTRING(REPLACE(\"Id\"::text, '-', ''), 1, 15) WHERE \"Code\" IS NULL OR \"Code\" = '';");

        migrationBuilder.CreateIndex(
            name: "IX_custom_areas_Code",
            schema: "venues",
            table: "custom_areas",
            column: "Code");

        migrationBuilder.CreateIndex(
            name: "IX_custom_areas_IsActive",
            schema: "venues",
            table: "custom_areas",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_custom_areas_IsFocus",
            schema: "venues",
            table: "custom_areas",
            column: "IsFocus");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_custom_areas_Code",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropIndex(
            name: "IX_custom_areas_IsActive",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropIndex(
            name: "IX_custom_areas_IsFocus",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropColumn(
            name: "Code",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropColumn(
            name: "DisplayOrder",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropColumn(
            name: "IsActive",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropColumn(
            name: "IsCustom",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.DropColumn(
            name: "IsFocus",
            schema: "venues",
            table: "custom_areas");

        migrationBuilder.AlterColumn<Guid>(
            name: "AddedByUserId",
            schema: "venues",
            table: "custom_areas",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
