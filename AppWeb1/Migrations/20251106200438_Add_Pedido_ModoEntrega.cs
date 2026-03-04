using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppWeb1.Migrations
{
    /// <inheritdoc />
    public partial class Add_Pedido_ModoEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModoEntrega",
                table: "Pedido",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModoEntrega",
                table: "Pedido");
        }
    }
}
