using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockNotificationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastNotifiedVariant",
                table: "StockMonitors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockNotificationHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockMonitorId = table.Column<int>(type: "INTEGER", nullable: false),
                    VariantName = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousAvailability = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentAvailability = table.Column<bool>(type: "INTEGER", nullable: false),
                    StockChangeAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotificationSentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockNotificationHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockNotificationHistories_StockMonitors_StockMonitorId",
                        column: x => x.StockMonitorId,
                        principalTable: "StockMonitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockNotificationHistories_StockMonitorId_VariantName_StockChangeAt",
                table: "StockNotificationHistories",
                columns: new[] { "StockMonitorId", "VariantName", "StockChangeAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockNotificationHistories");

            migrationBuilder.DropColumn(
                name: "LastNotifiedVariant",
                table: "StockMonitors");
        }
    }
}
