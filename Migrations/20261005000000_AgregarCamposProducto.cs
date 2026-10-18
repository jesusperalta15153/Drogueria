using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drogueria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCamposProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Miligramos",
                table: "productos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StockMinimo",
                table: "productos",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "LimiteMaximoPorPedido",
                table: "productos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Miligramos",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "StockMinimo",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "LimiteMaximoPorPedido",
                table: "productos");
        }
    }
}
