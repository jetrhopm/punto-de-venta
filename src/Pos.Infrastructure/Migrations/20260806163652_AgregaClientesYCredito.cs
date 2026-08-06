using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaClientesYCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "pos",
                table: "sale",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_transaction",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_transaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_transaction_customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "pos",
                        principalTable: "customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_transaction_sale_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "pos",
                        principalTable: "sale",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sale_CustomerId",
                schema: "pos",
                table: "sale",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transaction_CustomerId",
                schema: "pos",
                table: "credit_transaction",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_transaction_OperationId",
                schema: "pos",
                table: "credit_transaction",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_transaction_SaleId",
                schema: "pos",
                table: "credit_transaction",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_Name",
                schema: "pos",
                table: "customer",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_sale_customer_CustomerId",
                schema: "pos",
                table: "sale",
                column: "CustomerId",
                principalSchema: "pos",
                principalTable: "customer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sale_customer_CustomerId",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropTable(
                name: "credit_transaction",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_sale_CustomerId",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "pos",
                table: "sale");
        }
    }
}
