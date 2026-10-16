namespace HouseRentalSystem.Models
{
    // Populate this in AdminController.Index() with system-wide counts for the admin dashboard.
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalOwners { get; set; }
        public int TotalTenants { get; set; }
        public int BlockedUsers { get; set; }
        public int TotalProperties { get; set; }
        public int AvailableProperties { get; set; }
        public int ActiveAgreements { get; set; }
        public int PendingApplications { get; set; }
        public int PendingRequests { get; set; }
        public int AwaitingSupportReply { get; set; }
        public int PendingBookingApprovals { get; set; }
        public int PendingMaintenanceRequests { get; set; }
    }
}
