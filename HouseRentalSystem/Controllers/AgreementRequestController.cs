using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Controllers
{
    // Lets a tenant or owner raise a cancellation or refund request against their own
    // tenancy, for an admin to approve or reject - see AdminController.Requests for that side.
    [Authorize(Roles = "Owner,Tenant")]
    public class AgreementRequestController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AgreementRequestController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /AgreementRequest/Create?agreementId=5&type=Cancellation
        public async Task<IActionResult> Create(int agreementId, RequestType type)
        {
            var agreement = await GetPartyAgreementAsync(agreementId);
            if (agreement == null)
            {
                return Forbid();
            }

            // Refunds come out of the platform to the tenant - an owner has nothing to be
            // refunded, so only tenants can raise one.
            if (type == RequestType.Refund && !User.IsInRole("Tenant"))
            {
                return Forbid();
            }

            if (type == RequestType.Cancellation && agreement.Status != AgreementStatus.Active)
            {
                TempData["Notification"] = "This tenancy isn't active, so it can't be cancelled.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            if (type == RequestType.Refund && agreement.Status != AgreementStatus.Active)
            {
                TempData["Notification"] = "This tenancy has already ended, so no further refund requests can be raised on it.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            var duplicate = await _context.AgreementRequests.AnyAsync(r =>
                r.AgreementID == agreementId && r.RequestType == type && r.Status == RequestStatus.Pending);
            if (duplicate)
            {
                TempData["Notification"] = "There's already a pending request of this type on this tenancy.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            return View(new AgreementRequestViewModel
            {
                AgreementID = agreement.AgreementID,
                RequestType = type,
                PropertyTitle = agreement.Application.Property.Title,
                DepositBalance = agreement.DepositBalance,
                RequestedAmount = type == RequestType.Refund && agreement.DepositBalance > 0 ? agreement.DepositBalance : null
            });
        }

        // POST: /AgreementRequest/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AgreementRequestViewModel model)
        {
            var agreement = await GetPartyAgreementAsync(model.AgreementID);
            if (agreement == null)
            {
                return Forbid();
            }

            if (model.RequestType == RequestType.Refund && !User.IsInRole("Tenant"))
            {
                return Forbid();
            }

            if (model.RequestType == RequestType.Refund && agreement.Status != AgreementStatus.Active)
            {
                TempData["Notification"] = "This tenancy has already ended, so no further refund requests can be raised on it.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
            }

            if (model.RequestType == RequestType.Refund && model.RequestedAmount == null)
            {
                ModelState.AddModelError(nameof(model.RequestedAmount), "Enter the amount you're requesting.");
            }

            if (!ModelState.IsValid)
            {
                model.PropertyTitle = agreement.Application.Property.Title;
                model.DepositBalance = agreement.DepositBalance;
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var request = new AgreementRequest
            {
                AgreementID = agreement.AgreementID,
                RequestedByUserID = userId,
                RequestType = model.RequestType,
                Reason = model.Reason.Trim(),
                RequestedAmount = model.RequestType == RequestType.Refund ? model.RequestedAmount : null,
                Status = RequestStatus.Pending,
                SubmittedDate = DateTime.Now
            };

            _context.AgreementRequests.Add(request);
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Your request has been sent to the admin for review.";
            return RedirectToAction("Details", "RentalAgreement", new { applicationId = agreement.ApplicationID });
        }

        // GET: /AgreementRequest/MyRequests
        public async Task<IActionResult> MyRequests()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var requests = await _context.AgreementRequests
                .Include(r => r.Agreement).ThenInclude(a => a.Application).ThenInclude(app => app.Property)
                .Where(r => r.RequestedByUserID == userId)
                .OrderByDescending(r => r.SubmittedDate)
                .ToListAsync();

            return View(requests);
        }

        // A tenant/owner may only raise a request on an agreement they're actually a party to.
        private async Task<RentalAgreement> GetPartyAgreementAsync(int agreementId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var agreement = await _context.RentalAgreements
                .Include(a => a.Application).ThenInclude(app => app.Property)
                .FirstOrDefaultAsync(a => a.AgreementID == agreementId);

            if (agreement == null)
            {
                return null;
            }

            var isOwner = agreement.Application.Property.OwnerID == userId;
            var isTenant = agreement.Application.TenantID == userId;

            return (isOwner || isTenant) ? agreement : null;
        }
    }
}
