using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class AgregaCatalogoYLimitesDeInventario : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inventory_limit_change",
            schema: "pos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousMinimumStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                PreviousMaximumStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                MinimumStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                MaximumStock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_limit_change", x => x.Id);
                table.ForeignKey(name: "FK_inventory_limit_change_product_ProductId", column: x => x.ProductId, principalSchema: "pos", principalTable: "product", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_inventory_limit_change_user_account_UserId", column: x => x.UserId, principalSchema: "pos", principalTable: "user_account", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_inventory_limit_change_OperationId", schema: "pos", table: "inventory_limit_change", column: "OperationId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_inventory_limit_change_ProductId", schema: "pos", table: "inventory_limit_change", column: "ProductId");
        migrationBuilder.CreateIndex(name: "IX_inventory_limit_change_UserId", schema: "pos", table: "inventory_limit_change", column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "inventory_limit_change", schema: "pos");
}
