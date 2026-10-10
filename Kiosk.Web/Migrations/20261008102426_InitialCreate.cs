using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiosk.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OgrenciNo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AdSoyad = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kart_UID = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Face_Encoding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Yetki_Durumu = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Giris_Zamani = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Durum = table.Column<string>(type: "TEXT", nullable: false),
                    Kart_UID = table.Column<string>(type: "TEXT", nullable: false),
                    IsSimulation = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessGranted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_Giris_Zamani",
                table: "AccessLogs",
                column: "Giris_Zamani");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_UserId",
                table: "AccessLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Kart_UID",
                table: "Users",
                column: "Kart_UID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OgrenciNo",
                table: "Users",
                column: "OgrenciNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
