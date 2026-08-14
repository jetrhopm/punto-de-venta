using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrigeUnicidadDeTurnosAbiertos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shift_RegisterId_Status",
                schema: "pos",
                table: "shift");

            migrationBuilder.CreateIndex(
                name: "IX_shift_RegisterId_Status",
                schema: "pos",
                table: "shift",
                columns: new[] { "RegisterId", "Status" },
                unique: true,
                filter: "\"Status\" = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_shift_RegisterId_Status",
                schema: "pos",
                table: "shift");

            migrationBuilder.CreateIndex(
                name: "IX_shift_RegisterId_Status",
                schema: "pos",
                table: "shift",
                columns: new[] { "RegisterId", "Status" },
                unique: true);
        }
    }
}
