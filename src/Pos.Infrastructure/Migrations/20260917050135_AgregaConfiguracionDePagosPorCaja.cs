using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaConfiguracionDePagosPorCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older installations can already contain one or more columns after
            // previous local updates.  The migration must remain safe in both cases.
            migrationBuilder.Sql("""
                ALTER TABLE pos.register
                    ADD COLUMN IF NOT EXISTS "CashPaymentEnabled" boolean NOT NULL DEFAULT TRUE,
                    ADD COLUMN IF NOT EXISTS "CardPaymentEnabled" boolean NOT NULL DEFAULT TRUE,
                    ADD COLUMN IF NOT EXISTS "TransferPaymentEnabled" boolean NOT NULL DEFAULT TRUE,
                    ADD COLUMN IF NOT EXISTS "CreditPaymentEnabled" boolean NOT NULL DEFAULT TRUE,
                    ADD COLUMN IF NOT EXISTS "MercadoPagoEnabled" boolean NOT NULL DEFAULT FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE pos.register
                    DROP COLUMN IF EXISTS "CashPaymentEnabled",
                    DROP COLUMN IF EXISTS "CardPaymentEnabled",
                    DROP COLUMN IF EXISTS "TransferPaymentEnabled",
                    DROP COLUMN IF EXISTS "CreditPaymentEnabled",
                    DROP COLUMN IF EXISTS "MercadoPagoEnabled";
                """);
        }
    }
}
