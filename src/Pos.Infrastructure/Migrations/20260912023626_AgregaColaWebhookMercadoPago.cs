using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaColaWebhookMercadoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mercado_pago_webhook",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercado_pago_webhook", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_webhook_EventKey",
                schema: "pos",
                table: "mercado_pago_webhook",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mercado_pago_webhook_Status_NextAttemptAtUtc",
                schema: "pos",
                table: "mercado_pago_webhook",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mercado_pago_webhook",
                schema: "pos");
        }
    }
}
