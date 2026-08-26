using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMonitorWorkerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastCheckError",
                table: "StockMonitors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCheckStatus",
                table: "StockMonitors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextCheckAt",
                table: "StockMonitors",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "StockMonitorVariantStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockMonitorId = table.Column<int>(type: "INTEGER", nullable: false),
                    VariantName = table.Column<string>(type: "TEXT", nullable: false),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMonitorVariantStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMonitorVariantStates_StockMonitors_StockMonitorId",
                        column: x => x.StockMonitorId,
                        principalTable: "StockMonitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMonitorVariantStates_StockMonitorId_VariantName",
                table: "StockMonitorVariantStates",
                columns: new[] { "StockMonitorId", "VariantName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMonitorVariantStates");

            migrationBuilder.DropColumn(
                name: "LastCheckError",
                table: "StockMonitors");

            migrationBuilder.DropColumn(
                name: "LastCheckStatus",
                table: "StockMonitors");

            migrationBuilder.DropColumn(
                name: "NextCheckAt",
                table: "StockMonitors");
        }
    }
}
