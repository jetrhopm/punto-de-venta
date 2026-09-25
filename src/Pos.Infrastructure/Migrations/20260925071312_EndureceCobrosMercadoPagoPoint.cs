using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EndureceCobrosMercadoPagoPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaleOperationId",
                schema: "pos",
                table: "mercado_pago_order",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Las órdenes creadas antes de esta versión usaban un solo
            // identificador para ticket e intento Point. Se conservan ligadas a
            // su venta histórica para que no queden huérfanas durante la mejora.
            migrationBuilder.Sql("""
                UPDATE pos.mercado_pago_order
                SET "SaleOperationId" = "OperationId"
                WHERE "SaleOperationId" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_order_SaleOperationId",
                schema: "pos",
                table: "mercado_pago_order",
                column: "SaleOperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mercado_pago_order_SaleOperationId",
                schema: "pos",
                table: "mercado_pago_order");

            migrationBuilder.DropColumn(
                name: "SaleOperationId",
                schema: "pos",
                table: "mercado_pago_order");
        }
    }
}
