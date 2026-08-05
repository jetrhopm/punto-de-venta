using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreaConfiguracionInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            migrationBuilder.CreateTable(
                name: "store",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_account",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedUserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsAdministrator = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_account", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "register",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_register", x => x.Id);
                    table.ForeignKey(
                        name: "FK_register_store_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "pos",
                        principalTable: "store",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_register_StoreId_Name",
                schema: "pos",
                table: "register",
                columns: new[] { "StoreId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_account_NormalizedUserName",
                schema: "pos",
                table: "user_account",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "register",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "user_account",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "store",
                schema: "pos");
        }
    }
}
