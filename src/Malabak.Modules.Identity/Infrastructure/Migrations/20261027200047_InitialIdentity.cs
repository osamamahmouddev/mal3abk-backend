using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Malabak.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "users",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SecurityStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsPhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                IsEmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LastPhoneChangeAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Roles = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "phone_change_requests",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                OldPhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                NewPhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                OldPhoneOtpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                NewPhoneOtpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                RecoveryTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_phone_change_requests", x => x.Id);
                table.ForeignKey(
                    name: "FK_phone_change_requests_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                ReplacedByTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_refresh_tokens_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_sessions",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_sessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_sessions_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_phone_change_requests_ExpiresAtUtc",
            schema: "identity",
            table: "phone_change_requests",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_phone_change_requests_Status",
            schema: "identity",
            table: "phone_change_requests",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_phone_change_requests_UserId",
            schema: "identity",
            table: "phone_change_requests",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_ExpiresAtUtc",
            schema: "identity",
            table: "refresh_tokens",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_FamilyId",
            schema: "identity",
            table: "refresh_tokens",
            column: "FamilyId");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_UserId_IsRevoked",
            schema: "identity",
            table: "refresh_tokens",
            columns: new[] { "UserId", "IsRevoked" });

        migrationBuilder.CreateIndex(
            name: "IX_user_sessions_LastActivityAtUtc",
            schema: "identity",
            table: "user_sessions",
            column: "LastActivityAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_user_sessions_UserId_IsRevoked",
            schema: "identity",
            table: "user_sessions",
            columns: new[] { "UserId", "IsRevoked" });

        migrationBuilder.CreateIndex(
            name: "IX_users_CreatedAtUtc",
            schema: "identity",
            table: "users",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_users_Email",
            schema: "identity",
            table: "users",
            column: "Email",
            unique: true,
            filter: "email IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_users_IsDeleted",
            schema: "identity",
            table: "users",
            column: "IsDeleted");

        migrationBuilder.CreateIndex(
            name: "IX_users_PhoneNumber",
            schema: "identity",
            table: "users",
            column: "PhoneNumber",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "phone_change_requests",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "refresh_tokens",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "user_sessions",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "users",
            schema: "identity");
    }
}
