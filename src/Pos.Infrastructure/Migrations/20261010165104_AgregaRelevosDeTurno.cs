using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaRelevosDeTurno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                schema: "pos",
                table: "shift",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cut");

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCashAtClose",
                schema: "pos",
                table: "shift",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shift_handoff",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToShiftId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DeliveredCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handoff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shift_handoff_register_RegisterId",
                        column: x => x.RegisterId,
                        principalSchema: "pos",
                        principalTable: "register",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shift_handoff_RegisterId_Status",
                schema: "pos",
                table: "shift_handoff",
                columns: new[] { "RegisterId", "Status" },
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_handoff",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                schema: "pos",
                table: "shift");

            migrationBuilder.DropColumn(
                name: "ExpectedCashAtClose",
                schema: "pos",
                table: "shift");
        }
    }
}
