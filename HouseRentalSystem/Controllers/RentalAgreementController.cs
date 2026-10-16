using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    public class RentalAgreementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ArrearsService _arrears;

        public RentalAgreementController(ApplicationDbContext context, ArrearsService arrears)
        {
            _context = context;
            _arrears = arrears;
        }

        // GET: /RentalAgreement/Details/5  (5 = ApplicationID)
        // Visible to the tenant who applied and the owner of the property, no one else.
        [Authorize(Roles = "Owner,Tenant")]
        public async Task<IActionResult> Details(int applicationId)
        {
            var agreement = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property).ThenInclude(p => p.Owner)
                .Include(a => a.Application).ThenInclude(app => app.Tenant)
                .Include(a => a.Payments)
                .Include(a => a.ArrearsNotices)
                .Include(a => a.Reviews)
                .Include(a => a.AgreementRequests)
                .FirstOrDefaultAsync(a => a.ApplicationID == applicationId);

            if (agreement == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var isOwner = agreement.Application.Property.OwnerID == userId;
            var isTenant = agreement.Application.TenantID == userId;

            if (!isOwner && !isTenant)
            {
                return Forbid();
            }

            // Bring the late-rent position up to date before rendering, so a missed month is
            // reflected the moment either party looks at the agreement.
            var summary = await _arrears.EvaluateAsync(agreement);
            ViewBag.ArrearsSummary = summary;
            ViewBag.IsOwnerView = isOwner;

            if (isTenant)
            {
                var unseen = agreement.ArrearsNotices.Count(n => !n.IsAcknowledged);
                if (unseen > 0)
                {
                    TempData["Notification"] = unseen == 1
                        ? "You have a new notice about this tenancy - see 'Late rent notices' below."
                        : $"You have {unseen} new notices about this tenancy - see 'Late rent notices' below.";
                    await _arrears.AcknowledgeAsync(agreement);
                }
            }

            return View(agreement);
        }

        // GET: /RentalAgreement/RentCollection
        // Owner-only: every tenancy on the owner's properties with its rent-collection status,
        // so unpaid months and the action taken against them are in one place.
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> RentCollection()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var summaries = await _arrears.EvaluateForOwnerAsync(ownerId);
            return View(summaries);
        }

        // POST: /RentalAgreement/TerminateForArrears/5  (5 = AgreementID)
        // Owner-only: ends the tenancy once a compound has been issued and gone unpaid past
        // its due date, per clause 3 of the agreement.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> TerminateForArrears(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var agreement = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property)
                .Include(a => a.Payments)
                .Include(a => a.ArrearsNotices)
                .FirstOrDefaultAsync(a => a.AgreementID == id);

            if (agreement == null)
            {
                return NotFound();
            }

            if (agreement.Application.Property.OwnerID != ownerId)
            {
                return Forbid();
            }

            var terminated = await _arrears.TerminateForArrearsAsync(agreement);

            TempData["Notification"] = terminated
                ? "Tenancy terminated for non-payment. The tenant will see this on the agreement page."
                : "This tenancy can't be terminated yet - a compound must have been issued and gone unpaid past its due date.";

            return RedirectToAction(nameof(Details), new { applicationId = agreement.ApplicationID });
        }
    }
}
