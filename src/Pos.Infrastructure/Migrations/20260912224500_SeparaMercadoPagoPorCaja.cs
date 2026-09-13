using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class SeparaMercadoPagoPorCaja : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "MercadoPagoEnabled",
            schema: "pos",
            table: "register",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Preserve the behavior of every existing register before Point is
        // separated. Credentials remain protected on store; this only copies
        // the previous availability switch to each register.
        migrationBuilder.Sql("""
            UPDATE pos."register" AS register
            SET "MercadoPagoEnabled" = store."MercadoPagoEnabled"
            FROM pos."store" AS store
            WHERE register."StoreId" = store."Id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MercadoPagoEnabled",
            schema: "pos",
            table: "register");
    }
}
