using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaAdministracionDeCajasYReemparejamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegisterId",
                schema: "pos",
                table: "pairing_code",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pairing_code_RegisterId",
                schema: "pos",
                table: "pairing_code",
                column: "RegisterId");

            migrationBuilder.AddForeignKey(
                name: "FK_pairing_code_register_RegisterId",
                schema: "pos",
                table: "pairing_code",
                column: "RegisterId",
                principalSchema: "pos",
                principalTable: "register",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pairing_code_register_RegisterId",
                schema: "pos",
                table: "pairing_code");

            migrationBuilder.DropIndex(
                name: "IX_pairing_code_RegisterId",
                schema: "pos",
                table: "pairing_code");

            migrationBuilder.DropColumn(
                name: "RegisterId",
                schema: "pos",
                table: "pairing_code");
        }
    }
}
