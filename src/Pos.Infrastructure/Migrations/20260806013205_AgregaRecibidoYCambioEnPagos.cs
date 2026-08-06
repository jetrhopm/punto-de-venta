using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaRecibidoYCambioEnPagos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                schema: "pos",
                table: "shift",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CountedCash",
                schema: "pos",
                table: "shift",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Difference",
                schema: "pos",
                table: "shift",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Change",
                schema: "pos",
                table: "payment",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Received",
                schema: "pos",
                table: "payment",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "cash_movement",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_movement_shift_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "pos",
                        principalTable: "shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movement_ShiftId",
                schema: "pos",
                table: "cash_movement",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movement",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                schema: "pos",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "CountedCash",
                schema: "pos",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "Difference",
                schema: "pos",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "Change",
                schema: "pos",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "Received",
                schema: "pos",
                table: "payment");
        }
    }
}
