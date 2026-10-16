using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseRentalSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgreementRequests",
                columns: table => new
                {
                    RequestID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgreementID = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserID = table.Column<int>(type: "int", nullable: false),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAdminID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgreementRequests", x => x.RequestID);
                    table.ForeignKey(
                        name: "FK_AgreementRequests_RentalAgreements_AgreementID",
                        column: x => x.AgreementID,
                        principalTable: "RentalAgreements",
                        principalColumn: "AgreementID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgreementRequests_Users_DecidedByAdminID",
                        column: x => x.DecidedByAdminID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgreementRequests_Users_RequestedByUserID",
                        column: x => x.RequestedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgreementRequests_AgreementID",
                table: "AgreementRequests",
                column: "AgreementID");

            migrationBuilder.CreateIndex(
                name: "IX_AgreementRequests_DecidedByAdminID",
                table: "AgreementRequests",
                column: "DecidedByAdminID");

            migrationBuilder.CreateIndex(
                name: "IX_AgreementRequests_RequestedByUserID",
                table: "AgreementRequests",
                column: "RequestedByUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgreementRequests");
        }
    }
}
