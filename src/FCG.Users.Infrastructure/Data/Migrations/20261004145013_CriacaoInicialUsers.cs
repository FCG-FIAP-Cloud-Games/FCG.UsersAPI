using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Arrays gerados pelo EF para migration executada uma única vez.

namespace FCG.Users.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriacaoInicialUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_Perfil",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_Perfil", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tb_Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CPF = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SenhaHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PerfilId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DataInativacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_Usuarios", x => x.Id);
                    table.CheckConstraint("CK_tb_Usuarios_CPF_Formato", "\"CPF\" ~ '^[0-9]{11}$'");
                    table.CheckConstraint("CK_tb_Usuarios_Email_Normalizado", "\"Email\" = lower(btrim(\"Email\"))");
                    table.ForeignKey(
                        name: "FK_tb_Usuarios_tb_Perfil_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "tb_Perfil",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_LogUsuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DataCriacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_LogUsuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tb_LogUsuarios_tb_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "tb_Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_Tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DataCriacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DataExpiracao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DataRevogacao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_Tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tb_Tokens_tb_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "tb_Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "tb_Perfil",
                columns: new[] { "Id", "Nome" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "Usuario" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "Administrador" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tb_LogUsuarios_UsuarioId_DataCriacao",
                table: "tb_LogUsuarios",
                columns: new[] { "UsuarioId", "DataCriacao" });

            migrationBuilder.CreateIndex(
                name: "IX_tb_Perfil_Nome",
                table: "tb_Perfil",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tb_Tokens_TokenHash",
                table: "tb_Tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tb_Tokens_UsuarioId",
                table: "tb_Tokens",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_tb_Usuarios_CPF",
                table: "tb_Usuarios",
                column: "CPF",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tb_Usuarios_Email",
                table: "tb_Usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tb_Usuarios_PerfilId",
                table: "tb_Usuarios",
                column: "PerfilId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_LogUsuarios");

            migrationBuilder.DropTable(
                name: "tb_Tokens");

            migrationBuilder.DropTable(
                name: "tb_Usuarios");

            migrationBuilder.DropTable(
                name: "tb_Perfil");
        }
    }
}
