using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class EliminaConfiguracionPerifericosGlobal : Migration
{
    private static readonly string[] Columns =
    [
        "CashDrawerEnabled", "CashDrawerModel", "CashDrawerPort", "CashDrawerPrinterName",
        "ScaleBaudRate", "ScaleDataBits", "ScaleEnabled", "ScaleParity", "ScalePort",
        "ScaleReadTimeoutMs", "ScaleStopBits", "ScaleTerminator", "ScaleUnit", "TicketWidthMm"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in Columns)
            migrationBuilder.DropColumn(name: column, schema: "pos", table: "store");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "CashDrawerEnabled", schema: "pos", table: "store", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "CashDrawerModel", schema: "pos", table: "store", type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "PrinterPulse");
        migrationBuilder.AddColumn<string>(name: "CashDrawerPort", schema: "pos", table: "store", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "USB");
        migrationBuilder.AddColumn<string>(name: "CashDrawerPrinterName", schema: "pos", table: "store", type: "character varying(260)", maxLength: 260, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<int>(name: "ScaleBaudRate", schema: "pos", table: "store", type: "integer", nullable: false, defaultValue: 9600);
        migrationBuilder.AddColumn<int>(name: "ScaleDataBits", schema: "pos", table: "store", type: "integer", nullable: false, defaultValue: 8);
        migrationBuilder.AddColumn<bool>(name: "ScaleEnabled", schema: "pos", table: "store", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "ScaleParity", schema: "pos", table: "store", type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "None");
        migrationBuilder.AddColumn<string>(name: "ScalePort", schema: "pos", table: "store", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<int>(name: "ScaleReadTimeoutMs", schema: "pos", table: "store", type: "integer", nullable: false, defaultValue: 1500);
        migrationBuilder.AddColumn<string>(name: "ScaleStopBits", schema: "pos", table: "store", type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "One");
        migrationBuilder.AddColumn<string>(name: "ScaleTerminator", schema: "pos", table: "store", type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "CRLF");
        migrationBuilder.AddColumn<string>(name: "ScaleUnit", schema: "pos", table: "store", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Kilogramo");
        migrationBuilder.AddColumn<int>(name: "TicketWidthMm", schema: "pos", table: "store", type: "integer", nullable: false, defaultValue: 80);
    }
}
