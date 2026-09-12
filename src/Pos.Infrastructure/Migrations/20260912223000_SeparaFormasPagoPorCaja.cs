using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class SeparaFormasPagoPorCaja : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "CardPaymentEnabled", schema: "pos", table: "register", type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "CashPaymentEnabled", schema: "pos", table: "register", type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "CreditPaymentEnabled", schema: "pos", table: "register", type: "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "TransferPaymentEnabled", schema: "pos", table: "register", type: "boolean", nullable: false, defaultValue: true);

        // Existing registers inherit the store-wide configuration once. After
        // this migration, each register is the authoritative payment profile.
        migrationBuilder.Sql("""
            UPDATE pos."register" AS register
            SET "CashPaymentEnabled" = store."CashPaymentEnabled",
                "CardPaymentEnabled" = store."CardPaymentEnabled",
                "TransferPaymentEnabled" = store."TransferPaymentEnabled",
                "CreditPaymentEnabled" = store."CreditPaymentEnabled"
            FROM pos."store" AS store
            WHERE register."StoreId" = store."Id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CardPaymentEnabled", schema: "pos", table: "register");
        migrationBuilder.DropColumn(name: "CashPaymentEnabled", schema: "pos", table: "register");
        migrationBuilder.DropColumn(name: "CreditPaymentEnabled", schema: "pos", table: "register");
        migrationBuilder.DropColumn(name: "TransferPaymentEnabled", schema: "pos", table: "register");
    }
}
