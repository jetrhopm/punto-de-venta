using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaEfectivoRetiradoEnCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashWithdrawn",
                schema: "pos",
                table: "purchase",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "pos",
                table: "purchase",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            // Compras anteriores pagadas desde caja retiraron exactamente el total.
            migrationBuilder.Sql("UPDATE pos.purchase SET \"CashWithdrawn\" = \"Total\" WHERE \"PaidFromCashRegister\" = TRUE;");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_CreatedAtUtc",
                schema: "pos",
                table: "purchase",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_UserId",
                schema: "pos",
                table: "purchase",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_CreatedAtUtc",
                schema: "pos",
                table: "purchase");

            migrationBuilder.DropIndex(
                name: "IX_purchase_UserId",
                schema: "pos",
                table: "purchase");

            migrationBuilder.DropColumn(
                name: "CashWithdrawn",
                schema: "pos",
                table: "purchase");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "pos",
                table: "purchase");
        }
    }
}
