using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaTicketsEnAtencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sale_draft",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_draft", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_draft_shift_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "pos",
                        principalTable: "shift",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_draft_user_account_UserId",
                        column: x => x.UserId,
                        principalSchema: "pos",
                        principalTable: "user_account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_draft_line",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_draft_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sale_draft_line_product_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "pos",
                        principalTable: "product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_draft_line_sale_draft_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "pos",
                        principalTable: "sale_draft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_OperationId",
                schema: "pos",
                table: "sale_draft",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_ShiftId_Status",
                schema: "pos",
                table: "sale_draft",
                columns: new[] { "ShiftId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_ShiftId_TicketNumber",
                schema: "pos",
                table: "sale_draft",
                columns: new[] { "ShiftId", "TicketNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_UserId",
                schema: "pos",
                table: "sale_draft",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_line_DraftId_ProductId",
                schema: "pos",
                table: "sale_draft_line",
                columns: new[] { "DraftId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sale_draft_line_ProductId",
                schema: "pos",
                table: "sale_draft_line",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_draft_line",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "sale_draft",
                schema: "pos");
        }
    }
}
