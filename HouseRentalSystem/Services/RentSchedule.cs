using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // Builds the up-front payment plan for a newly approved tenancy: the security deposit
    // plus one rent instalment for every month of the term. Having the whole schedule in the
    // database from day one is what lets the system notice a month going unpaid.
    public static class RentSchedule
    {
        // The whole schedule at once - used where an agreement is already fully approved
        // and active from the start (demo data), rather than built up incrementally.
        public static IEnumerable<Payment> Build(RentalAgreement agreement, int termMonths)
        {
            yield return BuildDeposit(agreement);

            foreach (var payment in BuildRentInstalments(agreement, termMonths))
            {
                yield return payment;
            }
        }

        // Just the deposit - added the moment the tenant accepts the agreement, before the
        // owner or admin have approved anything. See RentalApplicationController.Create.
        public static Payment BuildDeposit(RentalAgreement agreement)
        {
            return new Payment
            {
                AgreementID = agreement.AgreementID,
                DueDate = agreement.StartDate,
                Amount = agreement.DepositAmount,
                PaymentType = PaymentType.Deposit,
                PaymentStatus = PaymentStatus.Pending,
                RentMonthNumber = 0,
                ReceiptPath = string.Empty
            };
        }

        // Just the monthly rent instalments - added once an admin gives final approval and
        // the agreement goes live. See AdminController.ApproveBooking.
        public static IEnumerable<Payment> BuildRentInstalments(RentalAgreement agreement, int termMonths)
        {
            // Rent is payable in advance, so month 1 falls due on the move-in date itself
            // and each later instalment on the same day of the following months.
            for (var month = 1; month <= termMonths; month++)
            {
                yield return new Payment
                {
                    AgreementID = agreement.AgreementID,
                    DueDate = agreement.StartDate.AddMonths(month - 1),
                    Amount = agreement.MonthlyRent,
                    PaymentType = PaymentType.Rent,
                    PaymentStatus = PaymentStatus.Pending,
                    RentMonthNumber = month,
                    ReceiptPath = string.Empty
                };
            }
        }
    }
}
