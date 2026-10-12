using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaConteoInventarioMovil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_inventory_count_session",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MobileDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_inventory_count_session", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_inventory_count_session_mobile_device_MobileDeviceId",
                        column: x => x.MobileDeviceId,
                        principalSchema: "pos",
                        principalTable: "mobile_device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mobile_inventory_count",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_inventory_count", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_inventory_count_mobile_inventory_count_session_Sessi~",
                        column: x => x.SessionId,
                        principalSchema: "pos",
                        principalTable: "mobile_inventory_count_session",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mobile_inventory_count_product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "pos",
                        principalTable: "product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_inventory_count_OperationId",
                schema: "pos",
                table: "mobile_inventory_count",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_inventory_count_ProductId",
                schema: "pos",
                table: "mobile_inventory_count",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_inventory_count_SessionId_CreatedAtUtc",
                schema: "pos",
                table: "mobile_inventory_count",
                columns: new[] { "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_inventory_count_session_MobileDeviceId",
                schema: "pos",
                table: "mobile_inventory_count_session",
                column: "MobileDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_inventory_count_session_StoreId_StartedAtUtc",
                schema: "pos",
                table: "mobile_inventory_count_session",
                columns: new[] { "StoreId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_inventory_count",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "mobile_inventory_count_session",
                schema: "pos");
        }
    }
}
