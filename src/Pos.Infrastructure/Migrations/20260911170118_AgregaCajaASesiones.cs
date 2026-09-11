using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaCajaASesiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_session_UserId",
                schema: "pos",
                table: "session");

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                schema: "pos",
                table: "session",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegisterId",
                schema: "pos",
                table: "session",
                type: "uuid",
                nullable: true);

            // Existing single-register installations did not persist a session
            // register. Associate their active history with the primary register
            // before multi-register login restrictions are enforced.
            migrationBuilder.Sql("""
                UPDATE pos."session" AS session
                SET "RegisterId" = primary_register."Id"
                FROM (
                    SELECT "Id"
                    FROM pos."register" AS register
                    WHERE register."IsActive" = TRUE
                    ORDER BY EXISTS (
                        SELECT 1
                        FROM pos.device AS device
                        WHERE device."RegisterId" = register."Id"
                    ), register."Name", register."Id"
                    LIMIT 1
                ) AS primary_register
                WHERE session."RegisterId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_session_DeviceId",
                schema: "pos",
                table: "session",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_session_RegisterId",
                schema: "pos",
                table: "session",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_session_UserId_RegisterId",
                schema: "pos",
                table: "session",
                columns: new[] { "UserId", "RegisterId" });

            migrationBuilder.AddForeignKey(
                name: "FK_session_device_DeviceId",
                schema: "pos",
                table: "session",
                column: "DeviceId",
                principalSchema: "pos",
                principalTable: "device",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_session_register_RegisterId",
                schema: "pos",
                table: "session",
                column: "RegisterId",
                principalSchema: "pos",
                principalTable: "register",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_session_device_DeviceId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropForeignKey(
                name: "FK_session_register_RegisterId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropIndex(
                name: "IX_session_DeviceId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropIndex(
                name: "IX_session_RegisterId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropIndex(
                name: "IX_session_UserId_RegisterId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                schema: "pos",
                table: "session");

            migrationBuilder.DropColumn(
                name: "RegisterId",
                schema: "pos",
                table: "session");

            migrationBuilder.CreateIndex(
                name: "IX_session_UserId",
                schema: "pos",
                table: "session",
                column: "UserId");
        }
    }
}
