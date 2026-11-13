using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class PhoneChangeAttemptsAndTotpReplay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "TotpLastUsedStep",
            schema: "identity",
            table: "users",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AttemptsNew",
            schema: "identity",
            table: "phone_change_requests",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "AttemptsOld",
            schema: "identity",
            table: "phone_change_requests",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TotpLastUsedStep",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "AttemptsNew",
            schema: "identity",
            table: "phone_change_requests");

        migrationBuilder.DropColumn(
            name: "AttemptsOld",
            schema: "identity",
            table: "phone_change_requests");
    }
}
