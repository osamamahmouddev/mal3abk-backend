using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class SmsDispatches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sms_dispatches",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MaskedPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PhoneHash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Template = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CostEgp = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sms_dispatches", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sms_dispatches_CreatedAtUtc",
            schema: "identity",
            table: "sms_dispatches",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_sms_dispatches_PhoneHash_CreatedAtUtc",
            schema: "identity",
            table: "sms_dispatches",
            columns: new[] { "PhoneHash", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_sms_dispatches_ProviderMessageId",
            schema: "identity",
            table: "sms_dispatches",
            column: "ProviderMessageId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sms_dispatches",
            schema: "identity");
    }
}
