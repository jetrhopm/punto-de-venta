using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Pos.Infrastructure.Migrations;
public partial class AgregaConfiguracionTicket : Migration
{
    protected override void Up(MigrationBuilder m) { m.AddColumn<string>(name: "TicketHeader", schema: "pos", table: "store", maxLength: 300, nullable: false, defaultValue: ""); m.AddColumn<string>(name: "TicketFooter", schema: "pos", table: "store", maxLength: 300, nullable: false, defaultValue: "Gracias por su compra"); m.AddColumn<int>(name: "TicketWidthMm", schema: "pos", table: "store", nullable: false, defaultValue: 80); }
    protected override void Down(MigrationBuilder m) { m.DropColumn(name: "TicketHeader", schema: "pos", table: "store"); m.DropColumn(name: "TicketFooter", schema: "pos", table: "store"); m.DropColumn(name: "TicketWidthMm", schema: "pos", table: "store"); }
}
