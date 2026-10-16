using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    public class PropertyController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public PropertyController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // Rejects the whole submission if any attached photo isn't an allowed image type
        // within the size limit - see PhotoUpload.
        private static bool PhotosAreValid(List<IFormFile> photos, out string error)
        {
            error = string.Empty;
            if (photos == null)
            {
                return true;
            }

            foreach (var photo in photos.Where(p => p.Length > 0))
            {
                if (!PhotoUpload.IsValidImage(photo, out error))
                {
                    return false;
                }
            }

            return true;
        }

        // GET: /Property/Details/5
        // Public page - anyone (including guests) can view a property's details.
        public async Task<IActionResult> Details(int id)
        {
            var property = await _context.Properties
                .Include(p => p.Owner)
                .Include(p => p.Category)
                .Include(p => p.Photos)
                .Include(p => p.PropertyAmenities).ThenInclude(pa => pa.Amenity)
                .FirstOrDefaultAsync(p => p.PropertyID == id);

            if (property == null)
            {
                return NotFound();
            }

            var reviews = await _context.Reviews
                .Include(r => r.Reviewer)
                .Where(r => r.Agreement.Application.PropertyID == id)
                .OrderByDescending(r => r.ReviewDate)
                .ToListAsync();

            var hasExistingRequest = false;
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Tenant"))
            {
                var tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                hasExistingRequest = await _context.RentalApplications.AnyAsync(a =>
                    a.PropertyID == id &&
                    a.TenantID == tenantId &&
                    (a.Status == ApplicationStatus.AwaitingPayment || a.Status == ApplicationStatus.Pending
                        || a.Status == ApplicationStatus.PendingAdminApproval || a.Status == ApplicationStatus.Approved));
            }

            // Owner credibility strip on the listing ("hosting 3 properties since 2026").
            var ownerListingCount = property.Owner == null
                ? 0
                : await _context.Properties.CountAsync(p => p.OwnerID == property.OwnerID);

            // How many tenants gave each star rating, for the score breakdown bars.
            var distribution = new int[5];
            foreach (var review in reviews.Where(r => r.Rating is >= 1 and <= 5))
            {
                distribution[review.Rating - 1]++;
            }

            var model = new PropertyDetailsViewModel
            {
                PropertyID = property.PropertyID,
                Title = property.Title,
                Description = property.Description,
                Address = property.Address,
                City = property.City,
                CategoryName = property.Category?.CategoryName,
                RentPrice = property.RentPrice,
                Deposit = property.Deposit,
                NumBedrooms = property.NumBedrooms,
                NumBathrooms = property.NumBathrooms,
                OwnerName = property.Owner?.FullName,
                OwnerJoinedDate = property.Owner?.CreatedDate ?? property.CreatedDate,
                OwnerListingCount = ownerListingCount,
                ListedDate = property.CreatedDate,
                PhotoPaths = property.Photos?
                    .OrderByDescending(ph => ph.IsPrimary)
                    .Select(ph => ph.PhotoPath)
                    .ToList() ?? new List<string>(),
                AmenityNames = property.PropertyAmenities?.Select(pa => pa.Amenity.AmenityName).ToList() ?? new List<string>(),
                AverageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0,
                ReviewCount = reviews.Count,
                RatingDistribution = distribution,
                Reviews = reviews.Select(r => new ReviewSummary
                {
                    ReviewerName = r.Reviewer?.FullName,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    ReviewDate = r.ReviewDate
                }).ToList(),
                HasExistingRequest = hasExistingRequest,
                IsAvailable = property.IsAvailable
            };

            return View(model);
        }

        // GET: /Property/MyListings
        // Owner-only: shows properties belonging to the logged-in owner.
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> MyListings()
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var properties = await _context.Properties
                .Include(p => p.Photos)
                .Where(p => p.OwnerID == ownerId)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return View(properties);
        }

        // GET: /Property/Create
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
            ViewBag.Amenities = await _context.Amenities.ToListAsync();
            return View();
        }

        // POST: /Property/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Create(Property property, int[] selectedAmenities, List<IFormFile> photos)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
                ViewBag.Amenities = await _context.Amenities.ToListAsync();
                return View(property);
            }

            if (!PhotosAreValid(photos, out var photoError))
            {
                ModelState.AddModelError("photos", photoError);
                ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
                ViewBag.Amenities = await _context.Amenities.ToListAsync();
                return View(property);
            }

            property.OwnerID = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            property.CreatedDate = DateTime.Now;
            _context.Properties.Add(property);
            await _context.SaveChangesAsync();

            if (selectedAmenities != null)
            {
                foreach (var amenityId in selectedAmenities)
                {
                    _context.PropertyAmenities.Add(new PropertyAmenity
                    {
                        PropertyID = property.PropertyID,
                        AmenityID = amenityId
                    });
                }
            }

            if (photos != null && photos.Any(p => p.Length > 0))
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "properties", property.PropertyID.ToString());
                Directory.CreateDirectory(uploadsFolder);

                bool isFirst = true;
                foreach (var photo in photos.Where(p => p.Length > 0))
                {
                    var fileName = $"{Guid.NewGuid()}{PhotoUpload.SafeExtension(photo)}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await photo.CopyToAsync(stream);
                    }

                    _context.PropertyPhotos.Add(new PropertyPhoto
                    {
                        PropertyID = property.PropertyID,
                        PhotoPath = $"/uploads/properties/{property.PropertyID}/{fileName}",
                        IsPrimary = isFirst
                    });
                    isFirst = false;
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(MyListings));
        }

        // GET: /Property/Edit/5
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Edit(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await _context.Properties
                .Include(p => p.Photos)
                .Include(p => p.PropertyAmenities)
                .FirstOrDefaultAsync(p => p.PropertyID == id);

            if (property == null)
            {
                return NotFound();
            }

            if (property.OwnerID != ownerId)
            {
                return Forbid();
            }

            ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
            ViewBag.Amenities = await _context.Amenities.ToListAsync();
            ViewBag.SelectedAmenityIds = property.PropertyAmenities.Select(pa => pa.AmenityID).ToList();
            return View(property);
        }

        // POST: /Property/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Edit(int id, Property property, int[] selectedAmenities, List<IFormFile> photos)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var existing = await _context.Properties
                .Include(p => p.Photos)
                .Include(p => p.PropertyAmenities)
                .FirstOrDefaultAsync(p => p.PropertyID == id);

            if (existing == null)
            {
                return NotFound();
            }

            if (existing.OwnerID != ownerId)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
                ViewBag.Amenities = await _context.Amenities.ToListAsync();
                ViewBag.SelectedAmenityIds = selectedAmenities ?? Array.Empty<int>();
                property.PropertyID = id;
                property.Photos = existing.Photos;
                return View(property);
            }

            if (!PhotosAreValid(photos, out var photoError))
            {
                ModelState.AddModelError("photos", photoError);
                ViewBag.Categories = await _context.PropertyCategories.ToListAsync();
                ViewBag.Amenities = await _context.Amenities.ToListAsync();
                ViewBag.SelectedAmenityIds = selectedAmenities ?? Array.Empty<int>();
                property.PropertyID = id;
                property.Photos = existing.Photos;
                return View(property);
            }

            existing.Title = property.Title;
            existing.Description = property.Description;
            existing.Address = property.Address;
            existing.City = property.City;
            existing.CategoryID = property.CategoryID;
            existing.RentPrice = property.RentPrice;
            existing.Deposit = property.Deposit;
            existing.NumBedrooms = property.NumBedrooms;
            existing.NumBathrooms = property.NumBathrooms;

            _context.PropertyAmenities.RemoveRange(existing.PropertyAmenities);
            if (selectedAmenities != null)
            {
                foreach (var amenityId in selectedAmenities)
                {
                    _context.PropertyAmenities.Add(new PropertyAmenity
                    {
                        PropertyID = existing.PropertyID,
                        AmenityID = amenityId
                    });
                }
            }

            if (photos != null && photos.Any(p => p.Length > 0))
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "properties", existing.PropertyID.ToString());
                Directory.CreateDirectory(uploadsFolder);

                var hasPrimary = existing.Photos.Any(p => p.IsPrimary);
                foreach (var photo in photos.Where(p => p.Length > 0))
                {
                    var fileName = $"{Guid.NewGuid()}{PhotoUpload.SafeExtension(photo)}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await photo.CopyToAsync(stream);
                    }

                    _context.PropertyPhotos.Add(new PropertyPhoto
                    {
                        PropertyID = existing.PropertyID,
                        PhotoPath = $"/uploads/properties/{existing.PropertyID}/{fileName}",
                        IsPrimary = !hasPrimary
                    });
                    hasPrimary = true;
                }
            }

            await _context.SaveChangesAsync();

            TempData["Notification"] = "Listing updated.";
            return RedirectToAction(nameof(MyListings));
        }

        // POST: /Property/DeletePhoto/5  (photoId, propertyId)
        // Removes a single photo from a listing being edited; promotes the next photo to
        // primary if the one removed was the cover photo.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> DeletePhoto(int photoId, int propertyId)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var photo = await _context.PropertyPhotos
                .Include(p => p.Property)
                .FirstOrDefaultAsync(p => p.PhotoID == photoId);

            if (photo == null)
            {
                return NotFound();
            }

            if (photo.Property.OwnerID != ownerId)
            {
                return Forbid();
            }

            var wasPrimary = photo.IsPrimary;

            var relativePath = photo.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(_environment.WebRootPath, relativePath);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            _context.PropertyPhotos.Remove(photo);
            await _context.SaveChangesAsync();

            if (wasPrimary)
            {
                var next = await _context.PropertyPhotos.FirstOrDefaultAsync(p => p.PropertyID == propertyId);
                if (next != null)
                {
                    next.IsPrimary = true;
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(Edit), new { id = propertyId });
        }

        // POST: /Property/ToggleAvailability/5
        // Quick action from MyListings - marks a listing available/not available without
        // opening the full edit form.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> ToggleAvailability(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await _context.Properties.FirstOrDefaultAsync(p => p.PropertyID == id);
            if (property == null)
            {
                return NotFound();
            }

            if (property.OwnerID != ownerId)
            {
                return Forbid();
            }

            // Marking available again must not reopen a property that's already occupied
            // under an active tenancy - IsAvailable is also what gates new applications, so
            // flipping it back on here would let a second tenant apply for an occupied home.
            if (!property.IsAvailable)
            {
                var isOccupied = await _context.RentalAgreements.AnyAsync(a =>
                    a.Application.PropertyID == id && a.Status == AgreementStatus.Active);
                if (isOccupied)
                {
                    TempData["Notification"] = "This property has an active tenancy and can't be marked available.";
                    return RedirectToAction(nameof(MyListings));
                }
            }

            property.IsAvailable = !property.IsAvailable;
            await _context.SaveChangesAsync();

            TempData["Notification"] = property.IsAvailable
                ? "Listing marked as available."
                : "Listing marked as not available.";
            return RedirectToAction(nameof(MyListings));
        }

        // GET: /Property/Delete/5
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Delete(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await _context.Properties
                .Include(p => p.Photos)
                .FirstOrDefaultAsync(p => p.PropertyID == id);

            if (property == null)
            {
                return NotFound();
            }

            if (property.OwnerID != ownerId)
            {
                return Forbid();
            }

            ViewBag.HasBookingHistory = await _context.RentalApplications.AnyAsync(a => a.PropertyID == id);
            return View(property);
        }

        // POST: /Property/Delete/5
        // A listing that has ever received a booking request keeps its history (rejected
        // applications, past agreements, etc.), so it can only be marked unavailable, not
        // removed outright - otherwise those records would dangle or be silently deleted.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var property = await _context.Properties
                .Include(p => p.Photos)
                .Include(p => p.PropertyAmenities)
                .FirstOrDefaultAsync(p => p.PropertyID == id);

            if (property == null)
            {
                return NotFound();
            }

            if (property.OwnerID != ownerId)
            {
                return Forbid();
            }

            var hasApplications = await _context.RentalApplications.AnyAsync(a => a.PropertyID == id);
            if (hasApplications)
            {
                TempData["Notification"] = "This listing has booking history and can't be deleted - mark it as unavailable instead.";
                return RedirectToAction(nameof(MyListings));
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "properties", id.ToString());
            if (Directory.Exists(uploadsFolder))
            {
                Directory.Delete(uploadsFolder, true);
            }

            _context.PropertyAmenities.RemoveRange(property.PropertyAmenities);
            _context.Properties.Remove(property);
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Listing deleted.";
            return RedirectToAction(nameof(MyListings));
        }
    }
}
