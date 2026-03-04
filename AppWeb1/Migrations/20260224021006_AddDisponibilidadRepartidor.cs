using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppWeb1.Migrations
{
    /// <inheritdoc />
    public partial class AddDisponibilidadRepartidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Disponibilidad",
                table: "Repartidores",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disponibilidad",
                table: "Repartidores");
        }
    }
}
