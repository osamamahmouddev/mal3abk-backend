using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PlayerProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Area",
            schema: "identity",
            table: "users",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "City",
            schema: "identity",
            table: "users",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Language",
            schema: "identity",
            table: "users",
            type: "character varying(5)",
            maxLength: 5,
            nullable: false,
            defaultValue: "ar");

        migrationBuilder.AddColumn<DateTime>(
            name: "OnboardingCompletedAtUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Positions",
            schema: "identity",
            table: "users",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "SkillLevel",
            schema: "identity",
            table: "users",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Area",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "City",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "Language",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "OnboardingCompletedAtUtc",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "Positions",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "SkillLevel",
            schema: "identity",
            table: "users");
    }
}
