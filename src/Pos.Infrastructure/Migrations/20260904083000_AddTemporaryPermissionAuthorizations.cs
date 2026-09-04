using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pos.Infrastructure.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20260904083000_AddTemporaryPermissionAuthorizations")]
public partial class AddTemporaryPermissionAuthorizations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "GrantedByUserId",
            schema: "pos",
            table: "permission_assignment",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ExpiresAtUtc",
            schema: "pos",
            table: "permission_assignment",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GrantedByUserId", schema: "pos", table: "permission_assignment");
        migrationBuilder.DropColumn(name: "ExpiresAtUtc", schema: "pos", table: "permission_assignment");
    }
}
