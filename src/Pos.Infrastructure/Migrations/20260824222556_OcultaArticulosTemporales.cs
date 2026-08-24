using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcultaArticulosTemporales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemporary",
                schema: "pos",
                table: "product",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Los productos comunes creados por versiones anteriores ya eran artículos de una sola venta.
            migrationBuilder.Sql("UPDATE pos.product SET \"IsTemporary\" = TRUE WHERE \"IsCommonProduct\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE pos.product SET \"IsTemporary\" = FALSE;");
            migrationBuilder.DropColumn(
                name: "IsTemporary",
                schema: "pos",
                table: "product");
        }
    }
}
