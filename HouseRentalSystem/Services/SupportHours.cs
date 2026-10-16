namespace HouseRentalSystem.Services
{
    // When a human is expected to be watching the support inbox. Outside this window
    // (or before anyone from the team has addressed a message) an automatic reply is
    // sent instead, so the sender isn't left wondering whether their message landed.
    public static class SupportHours
    {
        public const int OpenHour = 9;
        public const int CloseHour = 19; // 7pm, exclusive

        public static bool IsOpenNow(DateTime? asOf = null)
        {
            var hour = (asOf ?? DateTime.Now).Hour;
            return hour >= OpenHour && hour < CloseHour;
        }

        public const string AwayMessage =
            "Thanks for reaching out! Our support team is available 9am to 7pm daily and is currently " +
            "offline. We've logged your message and someone will get back to you as soon as we're back online.";
    }
}
