using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drogueria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarUnidadMedidaProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnidadMedida",
                table: "productos",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "mg");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnidadMedida",
                table: "productos");
        }
    }
}
