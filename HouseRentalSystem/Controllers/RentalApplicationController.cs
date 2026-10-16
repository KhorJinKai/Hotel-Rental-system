using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    public class RentalApplicationController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RentalApplicationController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /RentalApplication/Create?propertyId=5&moveInDate=2026-08-01
        // Reached from the "Request to book" button on the property details page.
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Create(int propertyId, DateTime? moveInDate)
        {
            var property = await _context.Properties
                .Include(p => p.Photos)
                .FirstOrDefaultAsync(p => p.PropertyID == propertyId);

            if (property == null)
            {
                return NotFound();
            }

            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var alreadyRequested = await _context.RentalApplications.AnyAsync(a =>
                a.PropertyID == propertyId &&
                a.TenantID == tenantId &&
                (a.Status == ApplicationStatus.AwaitingPayment || a.Status == ApplicationStatus.Pending
                    || a.Status == ApplicationStatus.PendingAdminApproval || a.Status == ApplicationStatus.Approved));

            if (alreadyRequested)
            {
                TempData["Notification"] = "You already have a booking request for this property.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (!property.IsAvailable)
            {
                TempData["Notification"] = "This property is no longer available.";
                return RedirectToAction("Details", "Property", new { id = propertyId });
            }

            var model = new RentalApplicationViewModel
            {
                PropertyID = property.PropertyID,
                PropertyTitle = property.Title,
                PropertyCity = property.City,
                RentPrice = property.RentPrice,
                Deposit = property.Deposit,
                PrimaryPhotoPath = property.Photos?.FirstOrDefault(p => p.IsPrimary)?.PhotoPath
                                    ?? property.Photos?.FirstOrDefault()?.PhotoPath,
                MoveInDate = moveInDate ?? DateTime.Now.AddDays(14)
            };

            return View(model);
        }

        // POST: /RentalApplication/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Create(RentalApplicationViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Re-fetch photo path since hidden fields can be tampered with / lost on validation errors
                var invalidProperty = await _context.Properties.Include(p => p.Photos)
                    .FirstOrDefaultAsync(p => p.PropertyID == model.PropertyID);
                model.PrimaryPhotoPath = invalidProperty?.Photos?.FirstOrDefault(p => p.IsPrimary)?.PhotoPath;
                return View(model);
            }

            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var alreadyRequested = await _context.RentalApplications.AnyAsync(a =>
                a.PropertyID == model.PropertyID &&
                a.TenantID == tenantId &&
                (a.Status == ApplicationStatus.AwaitingPayment || a.Status == ApplicationStatus.Pending
                    || a.Status == ApplicationStatus.PendingAdminApproval || a.Status == ApplicationStatus.Approved));

            if (alreadyRequested)
            {
                TempData["Notification"] = "You already have a booking request for this property.";
                return RedirectToAction(nameof(MyBookings));
            }

            var property = await _context.Properties.FirstOrDefaultAsync(p => p.PropertyID == model.PropertyID);

            if (property == null || !property.IsAvailable)
            {
                TempData["Notification"] = "This property is no longer available.";
                return RedirectToAction("Details", "Property", new { id = model.PropertyID });
            }

            var application = new RentalApplication
            {
                PropertyID = model.PropertyID,
                TenantID = tenantId,
                MoveInDate = model.MoveInDate,
                RentalDurationMonths = model.RentalDurationMonths,
                Message = model.Message ?? string.Empty,
                Status = ApplicationStatus.AwaitingPayment,
                AppliedDate = DateTime.Now
            };

            _context.RentalApplications.Add(application);
            await _context.SaveChangesAsync();

            // The agreement and its deposit line are created straight away so the tenant can
            // review the actual terms and pay before the owner ever sees the request - not
            // reserved (Property.IsAvailable untouched) until that deposit actually clears,
            // see PaymentController.Success.
            var agreement = new RentalAgreement
            {
                ApplicationID = application.ApplicationID,
                StartDate = application.MoveInDate,
                EndDate = application.MoveInDate.AddMonths(application.RentalDurationMonths),
                MonthlyRent = property.RentPrice,
                DepositAmount = property.Deposit,
                DepositBalance = property.Deposit,
                Status = AgreementStatus.PendingApproval
            };

            _context.RentalAgreements.Add(agreement);
            await _context.SaveChangesAsync();

            _context.Payments.Add(RentSchedule.BuildDeposit(agreement));
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "RentalAgreement", new { applicationId = application.ApplicationID });
        }

        // POST: /RentalApplication/AcceptTerms/12  (Tenant ticks "I agree" on the tenancy agreement)
        // Required before the deposit can be paid - see RentalAgreement/Details.cshtml.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> AcceptTerms(int id, bool agree)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var application = await _context.RentalApplications
                .FirstOrDefaultAsync(a => a.ApplicationID == id);

            if (application == null)
            {
                return NotFound();
            }

            if (application.TenantID != tenantId)
            {
                return Forbid();
            }

            if (application.Status != ApplicationStatus.AwaitingPayment)
            {
                TempData["Notification"] = "This booking request has already moved past the agreement step.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = id });
            }

            if (!agree)
            {
                TempData["Notification"] = "You must agree to the tenancy agreement before continuing.";
                return RedirectToAction("Details", "RentalAgreement", new { applicationId = id });
            }

            application.TermsAcceptedDate = DateTime.Now;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent request already recorded the acceptance - nothing more to do.
            }

            return RedirectToAction("Details", "RentalAgreement", new { applicationId = id });
        }

        // GET: /RentalApplication/MyBookings
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> MyBookings()
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var applications = await _context.RentalApplications
                .Include(a => a.Property).ThenInclude(p => p.Photos)
                .Include(a => a.Agreement).ThenInclude(ag => ag.Payments)
                .Where(a => a.TenantID == tenantId)
                .OrderByDescending(a => a.AppliedDate)
                .ToListAsync();

            return View(applications);
        }

        // GET: /RentalApplication/OwnerRequests
        // Owner-only: shows booking requests submitted for any of the owner's properties.
        // AwaitingPayment requests are excluded - the owner only sees a request once the
        // tenant has accepted the agreement and paid the deposit.
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> OwnerRequests()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var applications = await _context.RentalApplications
                .Include(a => a.Property)
                .Include(a => a.Tenant)
                .Where(a => a.Property.OwnerID == ownerId && a.Status != ApplicationStatus.AwaitingPayment)
                .OrderByDescending(a => a.AppliedDate)
                .ToListAsync();

            return View(applications);
        }

        // POST: /RentalApplication/Approve/12  (Owner approves a pending application)
        // This isn't the final word - it hands the application to an admin for sign-off
        // (see AdminController.ApproveBooking) before the agreement actually gets created.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Approve(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var application = await _context.RentalApplications
                .Include(a => a.Property)
                .FirstOrDefaultAsync(a => a.ApplicationID == id);

            if (application == null)
            {
                return NotFound();
            }

            if (application.Property.OwnerID != ownerId)
            {
                return Forbid();
            }

            // Guards against double-processing (a double-click, or resubmitting via the
            // browser's back button) reserving the property twice for the same application.
            if (application.Status != ApplicationStatus.Pending)
            {
                TempData["Notification"] = "This application has already been processed.";
                return RedirectToAction(nameof(OwnerRequests));
            }

            // The property was already reserved (IsAvailable = false) the moment this
            // tenant's deposit cleared - see PaymentController.Success. A second tenant's
            // deposit payment can't have reserved it too in the meantime, since that path
            // auto-refunds and rejects theirs the instant it detects the property is taken.
            application.Status = ApplicationStatus.PendingAdminApproval;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This application was just updated elsewhere - please review its current status.";
                return RedirectToAction(nameof(OwnerRequests));
            }

            TempData["Notification"] = "Approved - sent to an admin for final review before the agreement goes live.";
            return RedirectToAction(nameof(OwnerRequests));
        }

        // POST: /RentalApplication/Reject/12  (Owner rejects a pending application with a reason)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var application = await _context.RentalApplications
                .Include(a => a.Property)
                .Include(a => a.Agreement)
                .FirstOrDefaultAsync(a => a.ApplicationID == id);

            if (application == null)
            {
                return NotFound();
            }

            if (application.Property.OwnerID != ownerId)
            {
                return Forbid();
            }

            // Once an application is approved it has a live agreement (and possibly
            // payments) attached - rejecting it afterwards would leave that agreement
            // pointing at a "Rejected" application, an inconsistent state.
            if (application.Status != ApplicationStatus.Pending)
            {
                TempData["Notification"] = "This application has already been processed.";
                return RedirectToAction(nameof(OwnerRequests));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Notification"] = "Please provide a reason for rejecting this application.";
                return RedirectToAction(nameof(OwnerRequests));
            }

            application.Status = ApplicationStatus.Rejected;
            application.RejectionReason = reason.Trim();

            // The tenant already paid the deposit to get this far - releasing the property
            // and refunding it are not optional side effects of rejecting, they're required
            // to leave the tenant whole.
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
                ReceiptPath = "Automatic refund - booking rejected by owner"
            });

            application.Agreement.DepositBalance = 0;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                TempData["Notification"] = "This application was just updated elsewhere - please review its current status.";
                return RedirectToAction(nameof(OwnerRequests));
            }

            return RedirectToAction(nameof(OwnerRequests));
        }
    }
}
