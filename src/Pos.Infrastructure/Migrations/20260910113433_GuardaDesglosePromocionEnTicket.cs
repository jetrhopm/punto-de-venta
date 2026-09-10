using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GuardaDesglosePromocionEnTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountTotal",
                schema: "pos",
                table: "sale_line",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalUnitPrice",
                schema: "pos",
                table: "sale_line",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromotionName",
                schema: "pos",
                table: "sale_line",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountTotal",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "OriginalUnitPrice",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "PromotionName",
                schema: "pos",
                table: "sale_line");
        }
    }
}
