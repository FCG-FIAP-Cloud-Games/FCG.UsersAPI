using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FCG.Users.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutboxUserCreated : Migration
    {
        private static readonly string[] ColunasIndicePendentes = ["OccurredAt", "Id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_Outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_Outbox", x => x.Id);
                    table.CheckConstraint("CK_tb_Outbox_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_tb_Outbox_Ids", "\"EventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CorrelationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_tb_Outbox_PayloadObjeto", "jsonb_typeof(\"Payload\") = 'object'");
                    table.ForeignKey(
                        name: "FK_tb_Outbox_tb_Usuarios_UserId",
                        column: x => x.UserId,
                        principalTable: "tb_Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tb_Outbox_EventId",
                table: "tb_Outbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tb_Outbox_Pendentes",
                table: "tb_Outbox",
                columns: ColunasIndicePendentes,
                filter: "\"PublishedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tb_Outbox_UserId",
                table: "tb_Outbox",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_Outbox");
        }
    }
}
