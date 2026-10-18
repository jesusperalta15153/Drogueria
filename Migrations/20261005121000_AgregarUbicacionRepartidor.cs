using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drogueria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUbicacionRepartidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepartidorId",
                table: "Pedidos",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "LatitudRepartidor",
                table: "Pedidos",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LongitudRepartidor",
                table: "Pedidos",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaUbicacionFecha",
                table: "Pedidos",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_RepartidorId",
                table: "Pedidos",
                column: "RepartidorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_Usuarios_RepartidorId",
                table: "Pedidos",
                column: "RepartidorId",
                principalTable: "Usuarios",
                principalColumn: "Uuid",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_Usuarios_RepartidorId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_RepartidorId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "RepartidorId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "LatitudRepartidor",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "LongitudRepartidor",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "UltimaUbicacionFecha",
                table: "Pedidos");
        }
    }
}
