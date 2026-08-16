using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaFoliosDeVenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NextSaleFolio",
                schema: "pos",
                table: "store",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "Folio",
                schema: "pos",
                table: "sale",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Las ventas anteriores a este cambio no tenían folio. Se conservan y
            // reciben un consecutivo único según su fecha antes de activar el índice.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAtUtc", "Id") AS "Folio"
                    FROM pos.sale
                )
                UPDATE pos.sale AS sale
                SET "Folio" = numbered."Folio"
                FROM numbered
                WHERE sale."Id" = numbered."Id";

                UPDATE pos.store
                SET "NextSaleFolio" = COALESCE((SELECT MAX("Folio") + 1 FROM pos.sale), 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_sale_Folio",
                schema: "pos",
                table: "sale",
                column: "Folio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sale_Folio",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "NextSaleFolio",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "Folio",
                schema: "pos",
                table: "sale");
        }
    }
}
