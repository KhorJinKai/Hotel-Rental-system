using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseRentalSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddRentArrears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DepositBalance",
                table: "RentalAgreements",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaymentDate",
                table: "Payments",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RentMonthNumber",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SettledFromDeposit",
                table: "Payments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ArrearsNotices",
                columns: table => new
                {
                    ArrearsNoticeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgreementID = table.Column<int>(type: "int", nullable: false),
                    PaymentID = table.Column<int>(type: "int", nullable: true),
                    NoticeType = table.Column<int>(type: "int", nullable: false),
                    IssuedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MonthsOverdue = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArrearsNotices", x => x.ArrearsNoticeID);
                    table.ForeignKey(
                        name: "FK_ArrearsNotices_Payments_PaymentID",
                        column: x => x.PaymentID,
                        principalTable: "Payments",
                        principalColumn: "PaymentID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArrearsNotices_RentalAgreements_AgreementID",
                        column: x => x.AgreementID,
                        principalTable: "RentalAgreements",
                        principalColumn: "AgreementID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArrearsNotices_AgreementID",
                table: "ArrearsNotices",
                column: "AgreementID");

            migrationBuilder.CreateIndex(
                name: "IX_ArrearsNotices_PaymentID",
                table: "ArrearsNotices",
                column: "PaymentID");

            // ---- Backfill existing rows -------------------------------------------------
            // Without this, every payment already in the database would carry the default
            // DueDate of 0001-01-01 and read as centuries overdue, and every agreement would
            // show a spent deposit.

            // PaymentDate used to double as the due date, so it is the due date's best source.
            migrationBuilder.Sql("UPDATE Payments SET DueDate = PaymentDate;");

            // It only means "date received" now, so clear it on anything not yet paid.
            migrationBuilder.Sql("UPDATE Payments SET PaymentDate = NULL WHERE PaymentStatus <> 0;");

            // The single rent row each agreement had was always the first month.
            migrationBuilder.Sql("UPDATE Payments SET RentMonthNumber = 1 WHERE PaymentType = 0;");

            // Nothing has been deducted from any deposit yet.
            migrationBuilder.Sql("UPDATE RentalAgreements SET DepositBalance = DepositAmount;");

            // Existing agreements only ever had a first month's rent row. Generate the rest of
            // the term so arrears can be detected on tenancies that predate this feature.
            migrationBuilder.Sql(@"
                WITH Months AS (
                    SELECT ra.AgreementID, app.RentalDurationMonths, ra.StartDate, ra.MonthlyRent,
                           CAST(2 AS int) AS MonthNo
                    FROM RentalAgreements ra
                    INNER JOIN RentalApplications app ON app.ApplicationID = ra.ApplicationID
                    WHERE app.RentalDurationMonths >= 2
                    UNION ALL
                    SELECT m.AgreementID, m.RentalDurationMonths, m.StartDate, m.MonthlyRent, m.MonthNo + 1
                    FROM Months m
                    WHERE m.MonthNo + 1 <= m.RentalDurationMonths
                )
                INSERT INTO Payments
                    (AgreementID, DueDate, PaymentDate, Amount, PaymentType, PaymentStatus,
                     SettledFromDeposit, RentMonthNumber, ReceiptPath)
                SELECT m.AgreementID, DATEADD(MONTH, m.MonthNo - 1, m.StartDate), NULL, m.MonthlyRent,
                       0, 1, 0, m.MonthNo, ''
                FROM Months m
                OPTION (MAXRECURSION 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArrearsNotices");

            // Drop the generated instalments and any compounds; before this migration an
            // agreement only carried its deposit and first month's rent.
            migrationBuilder.Sql("DELETE FROM Payments WHERE RentMonthNumber >= 2 OR PaymentType = 2;");

            // PaymentDate is about to become non-nullable again.
            migrationBuilder.Sql("UPDATE Payments SET PaymentDate = DueDate WHERE PaymentDate IS NULL;");

            migrationBuilder.DropColumn(
                name: "DepositBalance",
                table: "RentalAgreements");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RentMonthNumber",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SettledFromDeposit",
                table: "Payments");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PaymentDate",
                table: "Payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
