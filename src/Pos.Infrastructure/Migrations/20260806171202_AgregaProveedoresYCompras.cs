using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaProveedoresYCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Cost",
                schema: "pos",
                table: "product",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "pos",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_line",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_line_product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "pos",
                        principalTable: "product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_line_purchase_PurchaseId",
                        column: x => x.PurchaseId,
                        principalSchema: "pos",
                        principalTable: "purchase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_OperationId",
                schema: "pos",
                table: "purchase",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_SupplierId",
                schema: "pos",
                table: "purchase",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_line_ProductId",
                schema: "pos",
                table: "purchase_line",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_line_PurchaseId",
                schema: "pos",
                table: "purchase_line",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_Name",
                schema: "pos",
                table: "supplier",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_line",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "purchase",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "Cost",
                schema: "pos",
                table: "product");
        }
    }
}
