using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    // Lets a tenant report a maintenance issue on a property they currently rent. The request
    // goes to an admin for review (see AdminController.Maintenance/MaintenanceReply) rather than
    // straight to the owner - the owner only sees a report once the admin has replied to the
    // tenant, via OwnerRequests below.
    public class MaintenanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public MaintenanceController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: /Maintenance/Create?propertyId=5
        // Reached from "Report a maintenance issue" on the rental agreement page.
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Create(int propertyId)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await GetActiveTenancyPropertyAsync(propertyId, tenantId);
            if (property == null)
            {
                return Forbid();
            }

            return View(new MaintenanceRequestViewModel
            {
                PropertyID = property.PropertyID,
                PropertyTitle = property.Title
            });
        }

        // POST: /Maintenance/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> Create(MaintenanceRequestViewModel model, IFormFile? photo)
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await GetActiveTenancyPropertyAsync(model.PropertyID, tenantId);
            if (property == null)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                model.PropertyTitle = property.Title;
                return View(model);
            }

            if (photo != null && photo.Length > 0 && !PhotoUpload.IsValidImage(photo, out var photoError))
            {
                ModelState.AddModelError("photo", photoError);
                model.PropertyTitle = property.Title;
                return View(model);
            }

            var request = new MaintenanceRequest
            {
                PropertyID = property.PropertyID,
                TenantID = tenantId,
                Title = model.Title.Trim(),
                Description = model.Description.Trim(),
                PhotoPath = string.Empty,
                Status = MaintenanceStatus.Open,
                SubmittedDate = DateTime.Now
            };

            _context.MaintenanceRequests.Add(request);
            await _context.SaveChangesAsync();

            if (photo != null && photo.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "maintenance", request.RequestID.ToString());
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{PhotoUpload.SafeExtension(photo)}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await photo.CopyToAsync(stream);
                }

                request.PhotoPath = $"/uploads/maintenance/{request.RequestID}/{fileName}";
                await _context.SaveChangesAsync();
            }

            TempData["Notification"] = "Your maintenance request has been sent for review. We'll reply here once it's been looked at.";
            return RedirectToAction(nameof(MyRequests));
        }

        // GET: /Maintenance/MyRequests
        [Authorize(Roles = "Tenant")]
        public async Task<IActionResult> MyRequests()
        {
            var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var requests = await _context.MaintenanceRequests
                .Include(r => r.Property)
                .Where(r => r.TenantID == tenantId)
                .OrderByDescending(r => r.SubmittedDate)
                .ToListAsync();

            return View(requests);
        }

        // GET: /Maintenance/OwnerRequests
        // Read-only report of what happened - only requests an admin has already reviewed
        // and replied to the tenant about show up here, so the owner sees the outcome
        // rather than an unverified stream of raw tenant complaints.
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> OwnerRequests()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var requests = await _context.MaintenanceRequests
                .Include(r => r.Property)
                .Include(r => r.Tenant)
                .Include(r => r.RepliedByAdmin)
                .Where(r => r.Property.OwnerID == ownerId && r.RepliedDate != null)
                .OrderByDescending(r => r.RepliedDate)
                .ToListAsync();

            return View(requests);
        }

        // A tenant may only report an issue on a property they currently have an active
        // (approved and not-yet-ended) tenancy on - reporting on someone else's listing,
        // or one they merely applied for, isn't allowed.
        private async Task<Property> GetActiveTenancyPropertyAsync(int propertyId, int tenantId)
        {
            var application = await _context.RentalApplications
                .Include(a => a.Property)
                .Include(a => a.Agreement)
                .FirstOrDefaultAsync(a =>
                    a.PropertyID == propertyId &&
                    a.TenantID == tenantId &&
                    a.Status == ApplicationStatus.Approved &&
                    a.Agreement != null &&
                    a.Agreement.Status == AgreementStatus.Active);

            return application?.Property;
        }
    }
}
