using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguraCajonDeDinero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CashDrawerEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CashDrawerModel",
                schema: "pos",
                table: "store",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "PrinterPulse");

            migrationBuilder.AddColumn<string>(
                name: "CashDrawerPort",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "USB");

            migrationBuilder.AddColumn<string>(
                name: "CashDrawerPrinterName",
                schema: "pos",
                table: "store",
                type: "character varying(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashDrawerEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashDrawerModel",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashDrawerPort",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashDrawerPrinterName",
                schema: "pos",
                table: "store");
        }
    }
}
