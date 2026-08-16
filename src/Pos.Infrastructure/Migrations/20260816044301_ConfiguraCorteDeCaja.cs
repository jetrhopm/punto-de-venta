using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguraCorteDeCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoAdjustCashDifference",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashLimit",
                schema: "pos",
                table: "store",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CashLimitEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CashLimitMessage",
                schema: "pos",
                table: "store",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "Realiza un retiro de efectivo (F8); se superó el límite permitido en caja.");

            migrationBuilder.AddColumn<bool>(
                name: "RequireCashCountOnClose",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoAdjustCashDifference",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashLimit",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashLimitEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CashLimitMessage",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "RequireCashCountOnClose",
                schema: "pos",
                table: "store");
        }
    }
}
