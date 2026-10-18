using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drogueria.Migrations
{
    public partial class AgregarMetodoPagoPedido : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetodoPago",
                table: "Pedidos",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReferenciaPago",
                table: "Pedidos",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetodoPago",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ReferenciaPago",
                table: "Pedidos");
        }
    }
}