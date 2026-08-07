using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Pos.Infrastructure.Migrations;
public partial class AgregaKits : Migration
{
    protected override void Up(MigrationBuilder m) { m.AddColumn<bool>(name: "IsKit", schema: "pos", table: "product", nullable: false, defaultValue: false); m.CreateTable(name: "kit_component", schema: "pos", columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), KitProductId = table.Column<Guid>(type: "uuid", nullable: false), ComponentProductId = table.Column<Guid>(type: "uuid", nullable: false), Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false) }, constraints: table => { table.PrimaryKey("PK_kit_component", x => x.Id); table.ForeignKey("FK_kit_component_kit", x => x.KitProductId, principalSchema: "pos", principalTable: "product", principalColumn: "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_kit_component_component", x => x.ComponentProductId, principalSchema: "pos", principalTable: "product", principalColumn: "Id", onDelete: ReferentialAction.Restrict); }); m.CreateIndex(name: "IX_kit_component_KitProductId_ComponentProductId", schema: "pos", table: "kit_component", columns: new[] { "KitProductId", "ComponentProductId" }, unique: true); }
    protected override void Down(MigrationBuilder m) { m.DropTable("kit_component", "pos"); m.DropColumn("IsKit", "product", "pos"); }
}
