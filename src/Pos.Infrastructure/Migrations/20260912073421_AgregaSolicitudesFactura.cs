using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaSolicitudesFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_request",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceiverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReceiverEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_request", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_request_SaleId",
                schema: "pos",
                table: "invoice_request",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_request_Status_RequestedAtUtc",
                schema: "pos",
                table: "invoice_request",
                columns: new[] { "Status", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_request",
                schema: "pos");
        }
    }
}
