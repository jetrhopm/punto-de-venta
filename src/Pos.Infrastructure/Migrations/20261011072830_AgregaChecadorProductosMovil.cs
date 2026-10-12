using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaChecadorProductosMovil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_device",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DeviceTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_device", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_pairing_code",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_pairing_code", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mobile_session",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MobileDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_session", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_device_DeviceTokenHash",
                schema: "pos",
                table: "mobile_device",
                column: "DeviceTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_device_StoreId_Name",
                schema: "pos",
                table: "mobile_device",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_pairing_code_CodeHash",
                schema: "pos",
                table: "mobile_pairing_code",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_session_TokenHash",
                schema: "pos",
                table: "mobile_session",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_session_UserId_MobileDeviceId",
                schema: "pos",
                table: "mobile_session",
                columns: new[] { "UserId", "MobileDeviceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_device",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "mobile_pairing_code",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "mobile_session",
                schema: "pos");
        }
    }
}
