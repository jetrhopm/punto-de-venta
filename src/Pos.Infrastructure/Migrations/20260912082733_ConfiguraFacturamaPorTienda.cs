using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguraFacturamaPorTienda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacturamaAccountEmail",
                schema: "pos",
                table: "store",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "FacturamaEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FacturamaEnvironment",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sandbox");

            migrationBuilder.AddColumn<string>(
                name: "FacturamaLastVerificationMessage",
                schema: "pos",
                table: "store",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FacturamaLastVerifiedAtUtc",
                schema: "pos",
                table: "store",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacturamaPasswordProtected",
                schema: "pos",
                table: "store",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FacturamaAccountEmail",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "FacturamaEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "FacturamaEnvironment",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "FacturamaLastVerificationMessage",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "FacturamaLastVerifiedAtUtc",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "FacturamaPasswordProtected",
                schema: "pos",
                table: "store");
        }
    }
}
