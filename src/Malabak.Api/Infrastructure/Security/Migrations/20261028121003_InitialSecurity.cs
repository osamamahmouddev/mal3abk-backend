using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Malabak.Api.Infrastructure.Security.Migrations;

/// <inheritdoc />
public partial class InitialSecurity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "security");

        migrationBuilder.CreateTable(
            name: "data_encryption_keys",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                WrappedKey = table.Column<byte[]>(type: "bytea", nullable: false),
                KeyEncryptionKeyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_data_encryption_keys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "data_protection_keys",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Xml = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_data_protection_keys", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_data_encryption_keys_single_active",
            schema: "security",
            table: "data_encryption_keys",
            column: "IsActive",
            unique: true,
            filter: "\"IsActive\" = TRUE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "data_encryption_keys",
            schema: "security");

        migrationBuilder.DropTable(
            name: "data_protection_keys",
            schema: "security");
    }
}
