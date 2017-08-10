using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaknetEmulator.Migrations
{
    public partial class InitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    rowId = table.Column<long>(nullable: false)
                        .Annotation("Autoincrement", true),
                    addr = table.Column<string>(nullable: true),
                    clientReqId = table.Column<long>(nullable: true),
                    gameId = table.Column<string>(nullable: true),
                    lastUpdate = table.Column<DateTime>(nullable: false),
                    rowPW = table.Column<string>(nullable: true),
                    timeoutSec = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.rowId);
                });

            migrationBuilder.CreateTable(
                name: "GameAttributes",
                columns: table => new
                {
                    gameCustFieldId = table.Column<string>(nullable: false),
                    GameDataRowId = table.Column<long>(nullable: false),
                    Key = table.Column<string>(nullable: true),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameAttributes", x => x.gameCustFieldId);
                    table.ForeignKey(
                        name: "FK_GameAttributes_Games_GameDataRowId",
                        column: x => x.GameDataRowId,
                        principalTable: "Games",
                        principalColumn: "rowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameAttributes_GameDataRowId",
                table: "GameAttributes",
                column: "GameDataRowId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameAttributes");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
