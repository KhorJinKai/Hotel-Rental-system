using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    // Admin-only maintenance: user account moderation and a system-wide view of listings.
    // Nothing here belongs to the tenant/owner booking flow - it exists so the Admin role
    // (seeded but otherwise unused) has real oversight functions, per the assignment's
    // requirement that every user role have protected, role-appropriate functionality.
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Admin
        public async Task<IActionResult> Index()
        {
            // "Awaiting reply" can't be expressed as a single COUNT - it depends on who sent
            // the most recent message on each open ticket - so open tickets are pulled back
            // and counted in memory. Fine at this scale; see AdminController.Support for the
            // same computation used by the actual inbox list.
            var openTickets = await _context.SupportTickets
                .Include(t => t.Messages)
                .Where(t => t.Status == TicketStatus.Open)
                .ToListAsync();
            var awaitingSupportReply = openTickets.Count(t => !LastMessageWasFromAdmin(t));

            var model = new AdminDashboardViewModel
            {
                TotalUsers = await _context.Users.CountAsync(),
                TotalOwners = await _context.Users.CountAsync(u => u.Role == UserRole.Owner),
                TotalTenants = await _context.Users.CountAsync(u => u.Role == UserRole.Tenant),
                BlockedUsers = await _context.Users.CountAsync(u => !u.IsActive),
                TotalProperties = await _context.Properties.CountAsync(),
                AvailableProperties = await _context.Properties.CountAsync(p => p.IsAvailable),
                ActiveAgreements = await _context.RentalAgreements.CountAsync(a => a.Status == AgreementStatus.Active),
                PendingApplications = await _context.RentalApplications.CountAsync(a => a.Status == ApplicationStatus.Pending),
                PendingRequests = await _context.AgreementRequests.CountAsync(r => r.Status == RequestStatus.Pending),
                AwaitingSupportReply = awaitingSupportReply,
                PendingBookingApprovals = await _context.RentalApplications.CountAsync(a => a.Status == ApplicationStatus.PendingAdminApproval),
                PendingMaintenanceRequests = await _context.MaintenanceRequests.CountAsync(r => r.RepliedDate == null)
            };

            return View(model);
        }

        // A ticket needs an admin's attention when its most recent message wasn't sent by
        // one - covers both a fresh tenant/owner message and a bot away-reply, since a bot
        // reply is just an automated acknowledgement, not an answer.
        private static bool LastMessageWasFromAdmin(SupportTicket ticket)
        {
            var last = ticket.Messages.OrderByDescending(m => m.SentDate).ThenByDescending(m => m.MessageID).FirstOrDefault();
            return last?.Sender == MessageSender.Admin;
        }

        // GET: /Admin/Users?search=&role=
        public async Task<IActionResult> Users(string search, UserRole? role)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
            }

            if (role.HasValue)
            {
                query = query.Where(u => u.Role == role.Value);
            }

            ViewBag.Search = search;
            ViewBag.Role = role;

            var users = await query
                .OrderBy(u => u.Role)
                .ThenBy(u => u.FullName)
                .ToListAsync();

            return View(users);
        }

        // POST: /Admin/ToggleUserActive/5
        // Blocks/unblocks a login. An admin cannot block their own account (would lock
        // them out with no other admin able to reverse it).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserActive(int id)
        {
            var currentAdminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (id == currentAdminId)
            {
                TempData["Notification"] = "You can't block your own account.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = !user.IsActive;

            if (user.IsActive)
            {
                // A re-activated account should not still be serving out a lockout timer.
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
            }

            await _context.SaveChangesAsync();

            TempData["Notification"] = user.IsActive
                ? $"{user.FullName}'s account has been unblocked."
                : $"{user.FullName}'s account has been blocked.";
            return RedirectToAction(nameof(Users));
        }

        // GET: /Admin/Properties?search=
        public async Task<IActionResult> Properties(string search)
        {
            var query = _context.Properties
                .Include(p => p.Owner)
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.Title.Contains(search) ||
                    p.City.Contains(search) ||
                    p.Owner.FullName.Contains(search));
            }

            ViewBag.Search = search;

            var properties = await query
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(properties);
        }

        // POST: /Admin/TogglePropertyAvailability/5
        // Lets an admin pull a listing from search results (e.g. reported content) without
        // deleting it or needing the owner's cooperation.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePropertyAvailability(int id)
        {
            var property = await _context.Properties.FindAsync(id);
            if (property == null)
            {
                return NotFound();
            }

            // Same guard as the owner's own toggle - IsAvailable also gates new applications,
            // so unhiding a property that's actually occupied (not just admin-hidden) would
            // let a second tenant apply for a home someone already lives in.
            if (!property.IsAvailable)
            {
                var isOccupied = await _context.RentalAgreements.AnyAsync(a =>
                    a.Application.PropertyID == id && a.Status == AgreementStatus.Active);
                if (isOccupied)
                {
                    TempData["Notification"] = "This listing has an active tenancy and can't be unhidden.";
                    return RedirectToAction(nameof(Properties));
                }
            }

            property.IsAvailable = !property.IsAvailable;
            await _context.SaveChangesAsync();

            TempData["Notification"] = property.IsAvailable
                ? "Listing unhidden - it will appear in search results again."
                : "Listing hidden from search results.";
            return RedirectToAction(nameof(Properties));
        }

        // GET: /Admin/Requests?status=&type=
        // Defaults to showing every request with pending ones surfaced first, so nothing
        // needing a decision gets buried under already-decided history.
        public async Task<IActionResult> Requests(RequestStatus? status, RequestType? type)
        {
            var query = _context.AgreementRequests
                .Include(r => r.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Property)
                .Include(r => r.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Tenant)
                .Include(r => r.RequestedBy)
                .Include(r => r.DecidedByAdmin)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }

            if (type.HasValue)
            {
                query = query.Where(r => r.RequestType == type.Value);
            }

            ViewBag.Status = status;
            ViewBag.Type = type;

            var requests = await query
                .OrderBy(r => r.Status == RequestStatus.Pending ? 0 : 1)
                .ThenByDescending(r => r.SubmittedDate)
                .ToListAsync();

            return View(requests);
        }

        // POST: /Admin/ApproveRequest/5
        // Cancellation: terminates the tenancy. Refund: records the payout and reduces the
        // deposit balance by the approved amount (which may be less than what was requested).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRequest(int id, decimal? approvedAmount, string adminNote)
        {
            var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var request = await _context.AgreementRequests
                .Include(r => r.Agreement)
                .FirstOrDefaultAsync(r => r.RequestID == id);

            if (request == null)
            {
                return NotFound();
            }

            if (request.Status != RequestStatus.Pending)
            {
                TempData["Notification"] = "This request has already been decided.";
                return RedirectToAction(nameof(Requests));
            }

            if (request.RequestType == RequestType.Cancellation)
            {
                request.Agreement.Status = AgreementStatus.Terminated;
            }
            else
            {
                var amount = approvedAmount ?? request.RequestedAmount ?? 0m;
                request.ApprovedAmount = amount;

                _context.Payments.Add(new Payment
                {
                    AgreementID = request.AgreementID,
                    DueDate = DateTime.Now,
                    PaymentDate = DateTime.Now,
                    Amount = amount,
                    PaymentType = PaymentType.Refund,
                    PaymentStatus = PaymentStatus.Paid,
                    RentMonthNumber = 0,
                    ReceiptPath = "Refund approved by admin"
                });

                request.Agreement.DepositBalance = Math.Max(0, request.Agreement.DepositBalance - amount);
            }

            request.Status = RequestStatus.Approved;
            request.AdminNote = adminNote;
            request.DecidedDate = DateTime.Now;
            request.DecidedByAdminID = adminId;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This request was just decided by someone else.";
                return RedirectToAction(nameof(Requests));
            }

            TempData["Notification"] = "Request approved.";
            return RedirectToAction(nameof(Requests));
        }

        // POST: /Admin/RejectRequest/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRequest(int id, string adminNote)
        {
            var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var request = await _context.AgreementRequests.FirstOrDefaultAsync(r => r.RequestID == id);
            if (request == null)
            {
                return NotFound();
            }

            if (request.Status != RequestStatus.Pending)
            {
                TempData["Notification"] = "This request has already been decided.";
                return RedirectToAction(nameof(Requests));
            }

            request.Status = RequestStatus.Rejected;
            request.AdminNote = adminNote;
            request.DecidedDate = DateTime.Now;
            request.DecidedByAdminID = adminId;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This request was just decided by someone else.";
                return RedirectToAction(nameof(Requests));
            }

            TempData["Notification"] = "Request rejected.";
            return RedirectToAction(nameof(Requests));
        }

        // GET: /Admin/Support?status=&awaitingOnly=
        // Ticket threads themselves live under SupportController.Details/Reply/Close, shared
        // with the tenant/owner side - this is just the admin-facing inbox list.
        public async Task<IActionResult> Support(TicketStatus? status, bool awaitingOnly = false)
        {
            var query = _context.SupportTickets
                .Include(t => t.User)
                .Include(t => t.Messages)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(t => t.Status == status.Value);
            }

            var tickets = await query.ToListAsync();

            if (awaitingOnly)
            {
                tickets = tickets.Where(t => t.Status == TicketStatus.Open && !LastMessageWasFromAdmin(t)).ToList();
            }

            ViewBag.Status = status;
            ViewBag.AwaitingOnly = awaitingOnly;

            var ordered = tickets.OrderByDescending(t => t.LastMessageDate).ToList();
            return View(ordered);
        }

        // GET: /Admin/Maintenance?status=
        // Every maintenance request raised by a tenant, regardless of which owner's property
        // it's against - an admin verifies each one and replies to the tenant here; only once
        // that reply is sent does the owner get a report (see MaintenanceReply below and
        // MaintenanceController.OwnerRequests).
        public async Task<IActionResult> Maintenance(MaintenanceStatus? status)
        {
            var query = _context.MaintenanceRequests
                .Include(r => r.Property).ThenInclude(p => p.Owner)
                .Include(r => r.Tenant)
                .Include(r => r.RepliedByAdmin)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }

            ViewBag.Status = status;

            var requests = await query
                .OrderBy(r => r.RepliedDate == null ? 0 : 1)
                .ThenByDescending(r => r.SubmittedDate)
                .ToListAsync();

            return View(requests);
        }

        // POST: /Admin/MaintenanceReply/5
        // Verifying the request and replying to the tenant are the same step - once sent, the
        // request also becomes visible to the owner as a report via MaintenanceController.OwnerRequests.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MaintenanceReply(int id, string adminReply, MaintenanceStatus status)
        {
            var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var request = await _context.MaintenanceRequests.FirstOrDefaultAsync(r => r.RequestID == id);
            if (request == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(adminReply))
            {
                TempData["Notification"] = "Enter a reply before sending.";
                return RedirectToAction(nameof(Maintenance));
            }

            var trimmedReply = adminReply.Trim();
            if (trimmedReply.Length > 1000)
            {
                TempData["Notification"] = "Reply must be 1000 characters or fewer.";
                return RedirectToAction(nameof(Maintenance));
            }

            request.AdminReply = trimmedReply;
            request.RepliedDate = DateTime.Now;
            request.RepliedByAdminID = adminId;
            request.Status = status;
            request.ResolvedDate = status == MaintenanceStatus.Resolved ? DateTime.Now : null;

            await _context.SaveChangesAsync();

            TempData["Notification"] = "Reply sent to the tenant - the owner has been sent a report of the outcome.";
            return RedirectToAction(nameof(Maintenance));
        }

        // GET: /Admin/BookingApprovals?status=
        // Final sign-off on a booking the owner has already approved - the deposit is already
        // paid and the agreement already exists, but it only goes live and gets its rent
        // schedule once it clears here (see ApproveBooking below).
        public async Task<IActionResult> BookingApprovals(ApplicationStatus? status)
        {
            var query = _context.RentalApplications
                .Include(a => a.Property).ThenInclude(p => p.Owner)
                .Include(a => a.Tenant)
                .Where(a => status.HasValue
                    ? a.Status == status.Value
                    : a.Status != ApplicationStatus.Pending && a.Status != ApplicationStatus.AwaitingPayment)
                .AsQueryable();

            ViewBag.Status = status;

            var applications = await query
                .OrderBy(a => a.Status == ApplicationStatus.PendingAdminApproval ? 0 : 1)
                .ThenByDescending(a => a.AppliedDate)
                .ToListAsync();

            return View(applications);
        }

        // POST: /Admin/ApproveBooking/12
        // The agreement and its deposit already exist - the tenant created and paid for them
        // up front (see RentalApplicationController.Create) - so this just activates the
        // agreement and adds the monthly rent instalments for the term.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveBooking(int id)
        {
            var application = await _context.RentalApplications
                .Include(a => a.Agreement)
                .FirstOrDefaultAsync(a => a.ApplicationID == id);

            if (application == null)
            {
                return NotFound();
            }

            if (application.Status != ApplicationStatus.PendingAdminApproval)
            {
                TempData["Notification"] = "This application isn't awaiting admin approval.";
                return RedirectToAction(nameof(BookingApprovals));
            }

            application.Status = ApplicationStatus.Approved;
            application.Agreement.Status = AgreementStatus.Active;

            // Just the rent instalments - the deposit line was added, and already paid,
            // when the tenant submitted the application.
            _context.Payments.AddRange(RentSchedule.BuildRentInstalments(application.Agreement, application.RentalDurationMonths));

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This application was just updated elsewhere - please review its current status.";
                return RedirectToAction(nameof(BookingApprovals));
            }

            TempData["Notification"] = "Booking approved - the rental agreement is now active.";
            return RedirectToAction(nameof(BookingApprovals));
        }

        // POST: /Admin/RejectBooking/12
        // Releases the property the owner's approval had reserved, and refunds the deposit
        // the tenant already paid to get this far - the tenancy isn't going ahead after all.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectBooking(int id, string adminNote)
        {
            var application = await _context.RentalApplications
                .Include(a => a.Property)
                .Include(a => a.Agreement)
                .FirstOrDefaultAsync(a => a.ApplicationID == id);

            if (application == null)
            {
                return NotFound();
            }

            if (application.Status != ApplicationStatus.PendingAdminApproval)
            {
                TempData["Notification"] = "This application isn't awaiting admin approval.";
                return RedirectToAction(nameof(BookingApprovals));
            }

            application.Status = ApplicationStatus.Rejected;
            application.RejectionReason = string.IsNullOrWhiteSpace(adminNote) ? null : adminNote.Trim();
            application.Property.IsAvailable = true;
            application.Agreement.Status = AgreementStatus.Terminated;

            _context.Payments.Add(new Payment
            {
                AgreementID = application.Agreement.AgreementID,
                DueDate = DateTime.Now,
                PaymentDate = DateTime.Now,
                Amount = application.Agreement.DepositBalance,
                PaymentType = PaymentType.Refund,
                PaymentStatus = PaymentStatus.Paid,
                RentMonthNumber = 0,
                ReceiptPath = "Automatic refund - booking rejected by admin"
            });

            application.Agreement.DepositBalance = 0;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This application was just updated elsewhere - please review its current status.";
                return RedirectToAction(nameof(BookingApprovals));
            }

            TempData["Notification"] = "Booking rejected. The listing is available again and the deposit has been refunded.";
            return RedirectToAction(nameof(BookingApprovals));
        }
    }
}
