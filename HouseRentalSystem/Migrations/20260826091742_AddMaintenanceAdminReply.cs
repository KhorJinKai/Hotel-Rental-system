using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseRentalSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceAdminReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminReply",
                table: "MaintenanceRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RepliedByAdminID",
                table: "MaintenanceRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedDate",
                table: "MaintenanceRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_RepliedByAdminID",
                table: "MaintenanceRequests",
                column: "RepliedByAdminID");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceRequests_Users_RepliedByAdminID",
                table: "MaintenanceRequests",
                column: "RepliedByAdminID",
                principalTable: "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceRequests_Users_RepliedByAdminID",
                table: "MaintenanceRequests");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceRequests_RepliedByAdminID",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "AdminReply",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "RepliedByAdminID",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "RepliedDate",
                table: "MaintenanceRequests");
        }
    }
}
