using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class AgregaPreciosMayoreo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "WholesalePrice", schema: "pos", table: "product", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "WholesaleMinimumQuantity", schema: "pos", table: "product", type: "numeric(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WholesalePrice", schema: "pos", table: "product");
        migrationBuilder.DropColumn(name: "WholesaleMinimumQuantity", schema: "pos", table: "product");
    }
}
