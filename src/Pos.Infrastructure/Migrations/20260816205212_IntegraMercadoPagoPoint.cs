using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntegraMercadoPagoPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoAccessTokenProtected",
                schema: "pos",
                table: "store",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "MercadoPagoEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoEnvironment",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Test");

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoOAuthState",
                schema: "pos",
                table: "store",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MercadoPagoOAuthStateExpiresAtUtc",
                schema: "pos",
                table: "store",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoOAuthVerifierProtected",
                schema: "pos",
                table: "store",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoRefreshTokenProtected",
                schema: "pos",
                table: "store",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MercadoPagoTokenExpiresAtUtc",
                schema: "pos",
                table: "store",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MercadoPagoUserId",
                schema: "pos",
                table: "store",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoTerminalId",
                schema: "pos",
                table: "register",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoTerminalLabel",
                schema: "pos",
                table: "register",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "mercado_pago_order",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderOrderId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    ProviderPaymentId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusDetail = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercado_pago_order", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mercado_pago_order_register_RegisterId",
                        column: x => x.RegisterId,
                        principalSchema: "pos",
                        principalTable: "register",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mercado_pago_order_store_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "pos",
                        principalTable: "store",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_order_OperationId",
                schema: "pos",
                table: "mercado_pago_order",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_order_ProviderOrderId",
                schema: "pos",
                table: "mercado_pago_order",
                column: "ProviderOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_order_RegisterId",
                schema: "pos",
                table: "mercado_pago_order",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_order_StoreId",
                schema: "pos",
                table: "mercado_pago_order",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mercado_pago_order",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "MercadoPagoAccessTokenProtected",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoEnvironment",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoOAuthState",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoOAuthStateExpiresAtUtc",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoOAuthVerifierProtected",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoRefreshTokenProtected",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTokenExpiresAtUtc",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoUserId",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTerminalId",
                schema: "pos",
                table: "register");

            migrationBuilder.DropColumn(
                name: "MercadoPagoTerminalLabel",
                schema: "pos",
                table: "register");
        }
    }
}
