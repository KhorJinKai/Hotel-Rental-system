using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    // Contact support - one ticket per conversation, replied to by an admin during
    // business hours. Outside those hours an automatic message is added immediately
    // so the sender isn't left wondering whether it went through - see SupportHours.
    [Authorize]
    public class SupportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SupportController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Support  (My tickets)
        [Authorize(Roles = "Owner,Tenant")]
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var tickets = await _context.SupportTickets
                .Include(t => t.Messages)
                .Where(t => t.UserID == userId)
                .OrderByDescending(t => t.LastMessageDate)
                .ToListAsync();

            return View(tickets);
        }

        // GET: /Support/Create
        [Authorize(Roles = "Owner,Tenant")]
        public IActionResult Create() => View(new SupportTicketViewModel());

        // POST: /Support/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Tenant")]
        public async Task<IActionResult> Create(SupportTicketViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var now = DateTime.Now;

            var ticket = new SupportTicket
            {
                UserID = userId,
                Subject = model.Subject.Trim(),
                Status = TicketStatus.Open,
                CreatedDate = now,
                LastMessageDate = now,
                Messages = new List<SupportMessage>
                {
                    new SupportMessage { Sender = MessageSender.User, SentByUserID = userId, Body = model.Message.Trim(), SentDate = now }
                }
            };

            if (!SupportHours.IsOpenNow(now))
            {
                ticket.Messages.Add(new SupportMessage { Sender = MessageSender.Bot, Body = SupportHours.AwayMessage, SentDate = now });
            }

            _context.SupportTickets.Add(ticket);
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Your message has been sent to support.";
            return RedirectToAction(nameof(Details), new { id = ticket.TicketID });
        }

        // GET: /Support/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var ticket = await _context.SupportTickets
                .Include(t => t.User)
                .Include(t => t.Messages).ThenInclude(m => m.SentBy)
                .FirstOrDefaultAsync(t => t.TicketID == id);

            if (ticket == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var isAdmin = User.IsInRole("Admin");

            if (!isAdmin && ticket.UserID != userId)
            {
                return Forbid();
            }

            ViewBag.IsAdminView = isAdmin;
            return View(ticket);
        }

        // POST: /Support/Reply/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int id, string body)
        {
            var ticket = await _context.SupportTickets
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.TicketID == id);

            if (ticket == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var isAdmin = User.IsInRole("Admin");

            if (!isAdmin && ticket.UserID != userId)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                TempData["Notification"] = "Enter a message before sending.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.Now;

            ticket.Messages.Add(new SupportMessage
            {
                Sender = isAdmin ? MessageSender.Admin : MessageSender.User,
                SentByUserID = userId,
                Body = body.Trim(),
                SentDate = now
            });
            ticket.LastMessageDate = now;

            // A reply from either side means the conversation isn't over - reopen it.
            ticket.Status = TicketStatus.Open;

            if (!isAdmin && !SupportHours.IsOpenNow(now))
            {
                ticket.Messages.Add(new SupportMessage { Sender = MessageSender.Bot, Body = SupportHours.AwayMessage, SentDate = now });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: /Support/Close/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Close(int id)
        {
            var ticket = await _context.SupportTickets.FindAsync(id);
            if (ticket == null)
            {
                return NotFound();
            }

            ticket.Status = TicketStatus.Closed;
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Ticket closed.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
