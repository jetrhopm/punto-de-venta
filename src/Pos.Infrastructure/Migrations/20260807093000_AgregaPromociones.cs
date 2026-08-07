using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Pos.Infrastructure.Migrations;
public partial class AgregaPromociones : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable(name: "promotion", schema: "pos", columns: table => new
    {
        Id = table.Column<Guid>(type: "uuid", nullable: false), ProductId = table.Column<Guid>(type: "uuid", nullable: false), Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false), Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false), StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), IsActive = table.Column<bool>(nullable: false)
    }, constraints: table => { table.PrimaryKey("PK_promotion", x => x.Id); table.ForeignKey(name: "FK_promotion_product_ProductId", x => x.ProductId, principalSchema: "pos", principalTable: "product", principalColumn: "Id", onDelete: ReferentialAction.Restrict); });
    protected override void Down(MigrationBuilder m) => m.DropTable("promotion", "pos");
}
