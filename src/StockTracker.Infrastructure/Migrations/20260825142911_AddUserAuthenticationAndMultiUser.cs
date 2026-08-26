using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuthenticationAndMultiUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create Users table first
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProtectedTelegramBotToken = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramChatId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            // 2. Insert initial system user with Id = 1 for existing monitors
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Email", "PasswordHash", "FirstName", "LastName", "IsActive", "CreatedAt" },
                values: new object[] { 1, "admin@stocktracker.local", "100000.k7K8zR0/g3z8lq+N7uR6rw==.u8Q7j8V3Z8R9k7L0g2w9Q8==", "System", "Admin", true, DateTime.UtcNow.ToString("O") });

            // 3. Add UserId columns with default value 1 for existing records
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "StockNotificationHistories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "StockMonitors",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_StockNotificationHistories_UserId",
                table: "StockNotificationHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMonitors_UserId",
                table: "StockMonitors",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMonitors_Users_UserId",
                table: "StockMonitors",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StockNotificationHistories_Users_UserId",
                table: "StockNotificationHistories",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMonitors_Users_UserId",
                table: "StockMonitors");

            migrationBuilder.DropForeignKey(
                name: "FK_StockNotificationHistories_Users_UserId",
                table: "StockNotificationHistories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_StockNotificationHistories_UserId",
                table: "StockNotificationHistories");

            migrationBuilder.DropIndex(
                name: "IX_StockMonitors_UserId",
                table: "StockMonitors");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "StockNotificationHistories");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "StockMonitors");
        }
    }
}
