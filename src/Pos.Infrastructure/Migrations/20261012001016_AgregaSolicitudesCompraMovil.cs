using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaSolicitudesCompraMovil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_purchase_request",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MobileDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedPayFromCashRegister = table.Column<bool>(type: "boolean", nullable: false),
                    ProposedCashWithdrawn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_purchase_request", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_purchase_request_mobile_device_MobileDeviceId",
                        column: x => x.MobileDeviceId,
                        principalSchema: "pos",
                        principalTable: "mobile_device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mobile_purchase_request_line",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_purchase_request_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_purchase_request_line_mobile_purchase_request_Reques~",
                        column: x => x.RequestId,
                        principalSchema: "pos",
                        principalTable: "mobile_purchase_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mobile_purchase_request_line_product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "pos",
                        principalTable: "product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_purchase_request_MobileDeviceId",
                schema: "pos",
                table: "mobile_purchase_request",
                column: "MobileDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_purchase_request_OperationId",
                schema: "pos",
                table: "mobile_purchase_request",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_purchase_request_StoreId_Status_CreatedAtUtc",
                schema: "pos",
                table: "mobile_purchase_request",
                columns: new[] { "StoreId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_purchase_request_line_ProductId",
                schema: "pos",
                table: "mobile_purchase_request_line",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_purchase_request_line_RequestId",
                schema: "pos",
                table: "mobile_purchase_request_line",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_purchase_request_line",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "mobile_purchase_request",
                schema: "pos");
        }
    }
}
