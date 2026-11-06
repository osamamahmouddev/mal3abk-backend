using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Teams.Infrastructure.Migrations;

/// <inheritdoc />
public partial class TeamSuspension : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "SuspendedAtUtc",
            schema: "teams",
            table: "teams",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SuspensionReason",
            schema: "teams",
            table: "teams",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SuspendedAtUtc",
            schema: "teams",
            table: "teams");

        migrationBuilder.DropColumn(
            name: "SuspensionReason",
            schema: "teams",
            table: "teams");
    }
}
