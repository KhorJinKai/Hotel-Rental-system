namespace HouseRentalSystem.Services
{
    // The late-rent rules the platform enforces, kept in one place so the agreement
    // terms shown to both parties and the code that acts on them cannot drift apart.
    //
    //   Missed month 1  ->  reminder sent to the tenant
    //   Missed month 2  ->  arrears deducted from the security deposit
    //   Missed month 3  ->  compound (late-payment penalty) charged to the tenant
    //
    public static class ArrearsPolicy
    {
        // Rent is due on the instalment date; it is only treated as missed once this
        // many days have passed without payment (matches clause 2 of the agreement).
        public const int GraceDays = 7;

        // A missed month escalates one rung of the ladder above. The reminder is issued
        // automatically by ArrearsService, once per overdue instalment.
        public const int ReminderStage = 1;
        public const int DepositDeductionStage = 2;
        public const int CompoundStage = 3;

        // Compound = 10% of the rent still outstanding, never less than RM 100.
        public const decimal CompoundRate = 0.10m;
        public const decimal MinimumCompound = 100m;

        // How long the tenant has to settle a compound once it is issued.
        public const int CompoundDueDays = 14;

        public static decimal CalculateCompound(decimal outstandingRent)
        {
            var compound = Math.Round(outstandingRent * CompoundRate, 2, MidpointRounding.AwayFromZero);
            return Math.Max(MinimumCompound, compound);
        }

        // Whole months elapsed between two dates, e.g. 1 Jan -> 28 Feb is 1 month.
        public static int WholeMonthsBetween(DateTime from, DateTime to)
        {
            if (to <= from)
            {
                return 0;
            }

            var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
            if (to.Day < from.Day)
            {
                months--;
            }

            return Math.Max(0, months);
        }
    }
}
