using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistraCajaProcesadoraEnDevoluciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessedRegisterId",
                schema: "pos",
                table: "sale_return",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_reversal_ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal",
                column: "ProcessedRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_return_ProcessedRegisterId",
                schema: "pos",
                table: "sale_return",
                column: "ProcessedRegisterId");

            migrationBuilder.AddForeignKey(
                name: "FK_sale_return_register_ProcessedRegisterId",
                schema: "pos",
                table: "sale_return",
                column: "ProcessedRegisterId",
                principalSchema: "pos",
                principalTable: "register",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sale_reversal_register_ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal",
                column: "ProcessedRegisterId",
                principalSchema: "pos",
                principalTable: "register",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_return_register_ProcessedRegisterId",
                schema: "pos",
                table: "sale_return");

            migrationBuilder.DropForeignKey(
                name: "FK_sale_reversal_register_ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal");

            migrationBuilder.DropIndex(
                name: "IX_sale_reversal_ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal");

            migrationBuilder.DropIndex(
                name: "IX_sale_return_ProcessedRegisterId",
                schema: "pos",
                table: "sale_return");

            migrationBuilder.DropColumn(
                name: "ProcessedRegisterId",
                schema: "pos",
                table: "sale_reversal");

            migrationBuilder.DropColumn(
                name: "ProcessedRegisterId",
                schema: "pos",
                table: "sale_return");
        }
    }
}
