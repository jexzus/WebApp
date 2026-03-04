using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppWeb1.Migrations
{
    /// <inheritdoc />
    public partial class AddTablaRepartidores_y_FK_en_Pedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vendedores_Usuario_UsuarioId",
                table: "Vendedores");

            migrationBuilder.DropIndex(
                name: "IX_Vendedores_UsuarioId",
                table: "Vendedores");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "Vendedores");

            migrationBuilder.AddColumn<int>(
                name: "IdRepartidor",
                table: "Pedido",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Repartidores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Apellido = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaAlta = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repartidores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repartidores_Usuario_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendedores_IdUsuario",
                table: "Vendedores",
                column: "IdUsuario");

            migrationBuilder.CreateIndex(
                name: "IX_Pedido_IdRepartidor",
                table: "Pedido",
                column: "IdRepartidor");

            migrationBuilder.CreateIndex(
                name: "IX_Repartidores_IdUsuario",
                table: "Repartidores",
                column: "IdUsuario");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedido_Repartidor",
                table: "Pedido",
                column: "IdRepartidor",
                principalTable: "Repartidores",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Vendedores_Usuario_IdUsuario",
                table: "Vendedores",
                column: "IdUsuario",
                principalTable: "Usuario",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedido_Repartidor",
                table: "Pedido");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendedores_Usuario_IdUsuario",
                table: "Vendedores");

            migrationBuilder.DropTable(
                name: "Repartidores");

            migrationBuilder.DropIndex(
                name: "IX_Vendedores_IdUsuario",
                table: "Vendedores");

            migrationBuilder.DropIndex(
                name: "IX_Pedido_IdRepartidor",
                table: "Pedido");

            migrationBuilder.DropColumn(
                name: "IdRepartidor",
                table: "Pedido");

            migrationBuilder.AddColumn<int>(
                name: "UsuarioId",
                table: "Vendedores",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendedores_UsuarioId",
                table: "Vendedores",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vendedores_Usuario_UsuarioId",
                table: "Vendedores",
                column: "UsuarioId",
                principalTable: "Usuario",
                principalColumn: "Id");
        }
    }
}
