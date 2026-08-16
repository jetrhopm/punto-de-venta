using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguraBascula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScaleBaudRate",
                schema: "pos",
                table: "store",
                type: "integer",
                nullable: false,
                defaultValue: 9600);

            migrationBuilder.AddColumn<int>(
                name: "ScaleDataBits",
                schema: "pos",
                table: "store",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<bool>(
                name: "ScaleEnabled",
                schema: "pos",
                table: "store",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ScaleParity",
                schema: "pos",
                table: "store",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "ScalePort",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScaleReadTimeoutMs",
                schema: "pos",
                table: "store",
                type: "integer",
                nullable: false,
                defaultValue: 1500);

            migrationBuilder.AddColumn<string>(
                name: "ScaleStopBits",
                schema: "pos",
                table: "store",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "One");

            migrationBuilder.AddColumn<string>(
                name: "ScaleTerminator",
                schema: "pos",
                table: "store",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CRLF");

            migrationBuilder.AddColumn<string>(
                name: "ScaleUnit",
                schema: "pos",
                table: "store",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Kilogramo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScaleBaudRate",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleDataBits",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleEnabled",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleParity",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScalePort",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleReadTimeoutMs",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleStopBits",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleTerminator",
                schema: "pos",
                table: "store");

            migrationBuilder.DropColumn(
                name: "ScaleUnit",
                schema: "pos",
                table: "store");
        }
    }
}
