using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class AgregaEmparejamientoLan : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device", schema: "pos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                DeviceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                DeviceTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_device", x => x.Id);
                table.ForeignKey(name: "FK_device_store_StoreId", columns: x => x.StoreId, principalSchema: "pos", principalTable: "store", principalColumns: new[] { "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_device_register_RegisterId", columns: x => x.RegisterId, principalSchema: "pos", principalTable: "register", principalColumns: new[] { "Id" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "pairing_code", schema: "pos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_pairing_code", x => x.Id);
                table.ForeignKey(name: "FK_pairing_code_store_StoreId", columns: x => x.StoreId, principalSchema: "pos", principalTable: "store", principalColumns: new[] { "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_pairing_code_user_account_CreatedByUserId", columns: x => x.CreatedByUserId, principalSchema: "pos", principalTable: "user_account", principalColumns: new[] { "Id" }, onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_device_DeviceTokenHash", schema: "pos", table: "device", column: "DeviceTokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_device_RegisterId", schema: "pos", table: "device", column: "RegisterId");
        migrationBuilder.CreateIndex(name: "IX_device_StoreId_Name", schema: "pos", table: "device", columns: new[] { "StoreId", "Name" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_pairing_code_CodeHash", schema: "pos", table: "pairing_code", column: "CodeHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_pairing_code_CreatedByUserId", schema: "pos", table: "pairing_code", column: "CreatedByUserId");
        migrationBuilder.CreateIndex(name: "IX_pairing_code_StoreId", schema: "pos", table: "pairing_code", column: "StoreId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "pairing_code", schema: "pos");
        migrationBuilder.DropTable(name: "device", schema: "pos");
    }
}
