using Microsoft.EntityFrameworkCore;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // The counts behind the admin's "needs attention" alert - the banner at the top of
    // every page and the red dots on the admin nav links. Same definitions the dashboard
    // tiles use (see AdminController.Index).
    public record AdminAlerts(
        int PendingBookingApprovals,
        int PendingMaintenance,
        int AwaitingSupportReply,
        int PendingRequests)
    {
        public int Total => PendingBookingApprovals + PendingMaintenance + AwaitingSupportReply + PendingRequests;
        public bool Any => Total > 0;
    }

    // Computes the admin alert counts once per request. Registered scoped, so the layout
    // (nav dots) and the alert banner share a single set of queries per page load.
    public class AdminAlertsService
    {
        private readonly ApplicationDbContext _context;
        private AdminAlerts? _cached;

        public AdminAlertsService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<AdminAlerts> GetAsync()
        {
            if (_cached != null)
            {
                return _cached;
            }

            var pendingBookingApprovals = await _context.RentalApplications
                .CountAsync(a => a.Status == ApplicationStatus.PendingAdminApproval);

            var pendingMaintenance = await _context.MaintenanceRequests
                .CountAsync(r => r.RepliedDate == null);

            var pendingRequests = await _context.AgreementRequests
                .CountAsync(r => r.Status == RequestStatus.Pending);

            // "Awaiting reply" depends on who sent the last message on each open ticket, so
            // it can't be a single COUNT - open tickets are pulled back and checked in memory
            // (same approach as AdminController.Index).
            var openTickets = await _context.SupportTickets
                .Include(t => t.Messages)
                .Where(t => t.Status == TicketStatus.Open)
                .ToListAsync();

            var awaitingSupportReply = openTickets.Count(t =>
            {
                var last = t.Messages
                    .OrderByDescending(m => m.SentDate)
                    .ThenByDescending(m => m.MessageID)
                    .FirstOrDefault();
                return last?.Sender != MessageSender.Admin;
            });

            _cached = new AdminAlerts(pendingBookingApprovals, pendingMaintenance, awaitingSupportReply, pendingRequests);
            return _cached;
        }
    }
}
