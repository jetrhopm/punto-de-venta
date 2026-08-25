using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pos.Infrastructure.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20260825090000_ReparaArticulosTemporales")]
public partial class ReparaArticulosTemporales : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Las ventas comunes necesitan conservar la partida histórica, pero nunca formar parte del catálogo ni inventario.
        migrationBuilder.Sql("UPDATE pos.product SET \"IsTemporary\" = TRUE WHERE \"IsCommonProduct\" = TRUE AND \"IsTemporary\" = FALSE;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
