using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaKardexYExistencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StockAfter",
                schema: "pos",
                table: "sale_line",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StockBefore",
                schema: "pos",
                table: "sale_line",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "SaleId",
                schema: "pos",
                table: "inventory_movement",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                schema: "pos",
                table: "inventory_movement",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "StockAfter",
                schema: "pos",
                table: "inventory_movement",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StockBefore",
                schema: "pos",
                table: "inventory_movement",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                schema: "pos",
                table: "inventory_movement",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockAfter",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "StockBefore",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "OperationId",
                schema: "pos",
                table: "inventory_movement");

            migrationBuilder.DropColumn(
                name: "StockAfter",
                schema: "pos",
                table: "inventory_movement");

            migrationBuilder.DropColumn(
                name: "StockBefore",
                schema: "pos",
                table: "inventory_movement");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "pos",
                table: "inventory_movement");

            migrationBuilder.AlterColumn<Guid>(
                name: "SaleId",
                schema: "pos",
                table: "inventory_movement",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
