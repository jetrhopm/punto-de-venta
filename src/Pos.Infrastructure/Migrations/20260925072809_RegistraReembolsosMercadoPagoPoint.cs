using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistraReembolsosMercadoPagoPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mercado_pago_refund",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MercadoPagoOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusDetail = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercado_pago_refund", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mercado_pago_refund_mercado_pago_order_MercadoPagoOrderId",
                        column: x => x.MercadoPagoOrderId,
                        principalSchema: "pos",
                        principalTable: "mercado_pago_order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mercado_pago_refund_sale_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "pos",
                        principalTable: "sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_refund_MercadoPagoOrderId",
                schema: "pos",
                table: "mercado_pago_refund",
                column: "MercadoPagoOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_refund_OperationId",
                schema: "pos",
                table: "mercado_pago_refund",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_refund_SaleId",
                schema: "pos",
                table: "mercado_pago_refund",
                column: "SaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mercado_pago_refund",
                schema: "pos");
        }
    }
}
