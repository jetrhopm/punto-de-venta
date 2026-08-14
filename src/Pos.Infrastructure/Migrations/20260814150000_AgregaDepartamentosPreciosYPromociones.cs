using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations;

public partial class AgregaDepartamentosPreciosYPromociones : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "department",
            schema: "pos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_department", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_department_NormalizedName", schema: "pos", table: "department", column: "NormalizedName", unique: true);
        migrationBuilder.AddColumn<Guid>(name: "DepartmentId", schema: "pos", table: "product", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "ProfitPercent", schema: "pos", table: "product", type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 20m);
        migrationBuilder.AddColumn<decimal>(name: "WholesaleProfitPercent", schema: "pos", table: "product", type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.CreateIndex(name: "IX_product_DepartmentId", schema: "pos", table: "product", column: "DepartmentId");
        migrationBuilder.AddForeignKey(name: "FK_product_department_DepartmentId", schema: "pos", table: "product", column: "DepartmentId", principalSchema: "pos", principalTable: "department", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddColumn<decimal>(name: "DiscountAmount", schema: "pos", table: "promotion", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "BuyQuantity", schema: "pos", table: "promotion", type: "numeric(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "PayQuantity", schema: "pos", table: "promotion", type: "numeric(18,3)", precision: 18, scale: 3, nullable: false, defaultValue: 0m);
        migrationBuilder.AlterColumn<DateTimeOffset>(name: "StartsAtUtc", schema: "pos", table: "promotion", type: "timestamp with time zone", nullable: true, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone");
        migrationBuilder.AlterColumn<DateTimeOffset>(name: "EndsAtUtc", schema: "pos", table: "promotion", type: "timestamp with time zone", nullable: true, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone");
        migrationBuilder.CreateIndex(name: "IX_promotion_Name", schema: "pos", table: "promotion", column: "Name", unique: true);

        var departments = new[]
        {
            ("10000000-0000-0000-0000-000000000001", "Abarrotes"), ("10000000-0000-0000-0000-000000000002", "Alimentos"), ("10000000-0000-0000-0000-000000000003", "Cremeria y salchichoneria"),
            ("10000000-0000-0000-0000-000000000004", "Fruteria"), ("10000000-0000-0000-0000-000000000005", "Limpieza"), ("10000000-0000-0000-0000-000000000006", "Panaderia"),
            ("10000000-0000-0000-0000-000000000007", "Medicamentos"), ("10000000-0000-0000-0000-000000000008", "Salud"), ("10000000-0000-0000-0000-000000000009", "Perfumeria"),
            ("10000000-0000-0000-0000-000000000010", "Lacteos"), ("10000000-0000-0000-0000-000000000011", "Bebidas energizantes"), ("10000000-0000-0000-0000-000000000012", "Bebidas azucaradas"),
            ("10000000-0000-0000-0000-000000000013", "Bebidas alcoholicas"), ("10000000-0000-0000-0000-000000000014", "Botanas"), ("10000000-0000-0000-0000-000000000015", "Dulceria"),
            ("10000000-0000-0000-0000-000000000016", "Granos y semillas"), ("10000000-0000-0000-0000-000000000017", "Higiene personal"), ("10000000-0000-0000-0000-000000000018", "Papeleria"),
            ("10000000-0000-0000-0000-000000000019", "Mascotas"), ("10000000-0000-0000-0000-000000000020", "Otros")
        };
        foreach (var department in departments)
        {
            migrationBuilder.Sql($"INSERT INTO pos.department (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\", \"CreatedAtUtc\") VALUES ('{department.Item1}', '{department.Item2.Replace("'", "''")}', '{department.Item2.ToUpperInvariant().Replace("'", "''")}', TRUE, CURRENT_TIMESTAMP) ON CONFLICT (\"NormalizedName\") DO NOTHING;");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_promotion_Name", schema: "pos", table: "promotion");
        migrationBuilder.AlterColumn<DateTimeOffset>(name: "StartsAtUtc", schema: "pos", table: "promotion", type: "timestamp with time zone", nullable: false, defaultValue: DateTimeOffset.UnixEpoch, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldNullable: true);
        migrationBuilder.AlterColumn<DateTimeOffset>(name: "EndsAtUtc", schema: "pos", table: "promotion", type: "timestamp with time zone", nullable: false, defaultValue: DateTimeOffset.UnixEpoch, oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldNullable: true);
        migrationBuilder.DropColumn(name: "DiscountAmount", schema: "pos", table: "promotion"); migrationBuilder.DropColumn(name: "BuyQuantity", schema: "pos", table: "promotion"); migrationBuilder.DropColumn(name: "PayQuantity", schema: "pos", table: "promotion");
        migrationBuilder.DropForeignKey(name: "FK_product_department_DepartmentId", schema: "pos", table: "product"); migrationBuilder.DropIndex(name: "IX_product_DepartmentId", schema: "pos", table: "product"); migrationBuilder.DropColumn(name: "DepartmentId", schema: "pos", table: "product"); migrationBuilder.DropColumn(name: "ProfitPercent", schema: "pos", table: "product"); migrationBuilder.DropColumn(name: "WholesaleProfitPercent", schema: "pos", table: "product");
        migrationBuilder.DropTable(name: "department", schema: "pos");
    }
}
