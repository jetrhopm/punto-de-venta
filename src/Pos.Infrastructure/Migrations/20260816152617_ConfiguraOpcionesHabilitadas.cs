using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguraOpcionesHabilitadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoPriceWithProfit",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommonProductsEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreditSalesEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultProfitPercent",
                schema: "pos",
                table: "store",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.AddColumn<string>(
                name: "InventoryCostMethod",
                schema: "pos",
                table: "store",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "WeightedAverage");

            migrationBuilder.AddColumn<bool>(
                name: "InventoryEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OccasionalNotice",
                schema: "pos",
                table: "store",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OccasionalNoticeEverySales",
                schema: "pos",
                table: "store",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "RoundSaleAmounts",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RoundingMode",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Tenths");

            migrationBuilder.AddColumn<bool>(
                name: "IsCommonProduct",
                schema: "pos",
                table: "product",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPriceWithProfit",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CommonProductsEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "CreditSalesEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "DefaultProfitPercent",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "InventoryCostMethod",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "InventoryEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "OccasionalNotice",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "OccasionalNoticeEverySales",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "RoundSaleAmounts",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "RoundingMode",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "IsCommonProduct",
                schema: "pos",
                table: "product");
        }
    }
}
