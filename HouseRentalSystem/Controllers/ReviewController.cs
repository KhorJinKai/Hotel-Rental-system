using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Controllers
{
    // Reviews can only be left by a tenant against their own rental agreement, and only
    // once per agreement - see the AnyAsync check in both actions below.
    [Authorize(Roles = "Tenant")]
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReviewController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Review/Create/5  (5 = AgreementID)
        public async Task<IActionResult> Create(int agreementId)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var agreement = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property).ThenInclude(p => p.Owner)
                .FirstOrDefaultAsync(a => a.AgreementID == agreementId);

            if (agreement == null)
            {
                return NotFound();
            }

            if (agreement.Application.TenantID != tenantId)
            {
                return Forbid();
            }

            var alreadyReviewed = await _context.Reviews
                .AnyAsync(r => r.AgreementID == agreementId && r.ReviewerID == tenantId);
            if (alreadyReviewed)
            {
                TempData["Notification"] = "You've already reviewed this stay.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            var model = new ReviewViewModel
            {
                AgreementID = agreement.AgreementID,
                PropertyTitle = agreement.Application.Property.Title,
                OwnerName = agreement.Application.Property.Owner.FullName,
                Rating = 5
            };
            return View(model);
        }

        // POST: /Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var agreement = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property).ThenInclude(p => p.Owner)
                .FirstOrDefaultAsync(a => a.AgreementID == model.AgreementID);

            if (agreement == null)
            {
                return NotFound();
            }

            if (agreement.Application.TenantID != tenantId)
            {
                return Forbid();
            }

            var alreadyReviewed = await _context.Reviews
                .AnyAsync(r => r.AgreementID == model.AgreementID && r.ReviewerID == tenantId);
            if (alreadyReviewed)
            {
                TempData["Notification"] = "You've already reviewed this stay.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            if (!ModelState.IsValid)
            {
                model.PropertyTitle = agreement.Application.Property.Title;
                model.OwnerName = agreement.Application.Property.Owner.FullName;
                return View(model);
            }

            _context.Reviews.Add(new Review
            {
                AgreementID = agreement.AgreementID,
                ReviewerID = tenantId,
                RevieweeID = agreement.Application.Property.OwnerID,
                Rating = model.Rating,
                Comment = model.Comment.Trim(),
                ReviewDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Thanks for your review!";
            return RedirectToAction("Details", "Property", new { id = agreement.Application.PropertyID });
        }
    }
}
