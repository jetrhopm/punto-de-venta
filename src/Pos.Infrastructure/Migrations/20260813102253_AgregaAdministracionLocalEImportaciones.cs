using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaAdministracionLocalEImportaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "pos",
                table: "store",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                schema: "pos",
                table: "store",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "pos",
                table: "store",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaxId",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "pos",
                table: "product",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumStock",
                schema: "pos",
                table: "product",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumStock",
                schema: "pos",
                table: "product",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimarySupplierId",
                schema: "pos",
                table: "product",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasure",
                schema: "pos",
                table: "product",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pieza");

            migrationBuilder.CreateTable(
                name: "import_batch",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    DuplicateRule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_batch_user_account_UserId",
                        column: x => x.UserId,
                        principalSchema: "pos",
                        principalTable: "user_account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_PrimarySupplierId",
                schema: "pos",
                table: "product",
                column: "PrimarySupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_import_batch_OperationId",
                schema: "pos",
                table: "import_batch",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_batch_UserId",
                schema: "pos",
                table: "import_batch",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_product_supplier_PrimarySupplierId",
                schema: "pos",
                table: "product",
                column: "PrimarySupplierId",
                principalSchema: "pos",
                principalTable: "supplier",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_product_supplier_PrimarySupplierId",
                schema: "pos",
                table: "product");

            migrationBuilder.DropTable(
                name: "import_batch",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_product_PrimarySupplierId",
                schema: "pos",
                table: "product");

            migrationBuilder.DropColumn(
                name: "Address",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "LegalName",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "TaxId",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "pos",
                table: "product");

            migrationBuilder.DropColumn(
                name: "MaximumStock",
                schema: "pos",
                table: "product");

            migrationBuilder.DropColumn(
                name: "MinimumStock",
                schema: "pos",
                table: "product");

            migrationBuilder.DropColumn(
                name: "PrimarySupplierId",
                schema: "pos",
                table: "product");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasure",
                schema: "pos",
                table: "product");
        }
    }
}
