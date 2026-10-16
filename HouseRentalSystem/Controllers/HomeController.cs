using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /  or  /Home/Index?Location=...&MoveInDate=...&CategoryID=...&MinPrice=...&SortBy=...
        public async Task<IActionResult> Index(
            string location,
            DateTime? moveInDate,
            int? categoryID,
            decimal? minPrice,
            decimal? maxPrice,
            int? bedrooms,
            int? minScore,
            int[] amenityIDs,
            string sortBy,
            int page = 1)
        {
            amenityIDs ??= Array.Empty<int>();

            var query = _context.Properties
                .Include(p => p.Photos)
                .Include(p => p.Category)
                .Include(p => p.PropertyAmenities).ThenInclude(pa => pa.Amenity)
                .Where(p => p.IsAvailable)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(location))
            {
                query = query.Where(p => p.City.Contains(location) || p.Address.Contains(location));
            }

            if (categoryID.HasValue)
            {
                query = query.Where(p => p.CategoryID == categoryID.Value);
            }

            if (minPrice.HasValue)
            {
                query = query.Where(p => p.RentPrice >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(p => p.RentPrice <= maxPrice.Value);
            }

            if (bedrooms.HasValue)
            {
                // "4+ bedrooms" is the last option, so the highest choice matches anything at or above it.
                query = query.Where(p => p.NumBedrooms >= bedrooms.Value);
            }

            // Every ticked amenity must be present (an AND filter, like a booking site's facilities list).
            foreach (var amenityId in amenityIDs.Distinct())
            {
                var id = amenityId;
                query = query.Where(p => p.PropertyAmenities.Any(pa => pa.AmenityID == id));
            }

            var properties = await query.ToListAsync();

            // One round trip for every review that belongs to any of the matched properties,
            // instead of a separate query per card.
            var propertyIds = properties.Select(p => p.PropertyID).ToList();
            var ratingLookup = (await _context.Reviews
                    .Where(r => propertyIds.Contains(r.Agreement.Application.PropertyID))
                    .Select(r => new { r.Agreement.Application.PropertyID, r.Rating })
                    .ToListAsync())
                .GroupBy(r => r.PropertyID)
                .ToDictionary(g => g.Key, g => new { Average = g.Average(x => x.Rating), Count = g.Count() });

            // Average rent per category, used for the "great value" badge and the struck-through price.
            var categoryAverages = properties
                .GroupBy(p => p.CategoryID)
                .ToDictionary(g => g.Key, g => g.Average(p => p.RentPrice));

            var cards = properties.Select(p =>
            {
                ratingLookup.TryGetValue(p.PropertyID, out var rating);
                var categoryAverage = categoryAverages.TryGetValue(p.CategoryID, out var avg) ? avg : p.RentPrice;

                return new PropertyCardViewModel
                {
                    PropertyID = p.PropertyID,
                    Title = p.Title,
                    City = p.City,
                    Address = p.Address,
                    CategoryName = p.Category?.CategoryName,
                    RentPrice = p.RentPrice,
                    Deposit = p.Deposit,
                    NumBedrooms = p.NumBedrooms,
                    NumBathrooms = p.NumBathrooms,
                    PrimaryPhotoPath = p.Photos?.FirstOrDefault(ph => ph.IsPrimary)?.PhotoPath
                                        ?? p.Photos?.FirstOrDefault()?.PhotoPath,
                    AmenityNames = p.PropertyAmenities?.Select(pa => pa.Amenity.AmenityName).ToList() ?? new List<string>(),
                    AverageRating = rating?.Average ?? 0,
                    ReviewCount = rating?.Count ?? 0,
                    IsNewListing = p.CreatedDate >= DateTime.Now.AddDays(-14),
                    IsGreatValue = p.RentPrice <= categoryAverage,
                    ComparablePrice = Math.Round(categoryAverage, 0)
                };
            }).ToList();

            if (minScore.HasValue)
            {
                cards = cards.Where(c => c.ReviewCount > 0 && c.ReviewScore >= minScore.Value).ToList();
            }

            cards = (sortBy switch
            {
                "price_low" => cards.OrderBy(c => c.RentPrice),
                "price_high" => cards.OrderByDescending(c => c.RentPrice),
                "rating" => cards.OrderByDescending(c => c.ReviewScore).ThenByDescending(c => c.ReviewCount),
                "newest" => cards.OrderByDescending(c => c.IsNewListing).ThenByDescending(c => c.PropertyID),
                // "Recommended" balances guest score against value, the way a booking site's default sort does.
                _ => cards.OrderByDescending(c => c.ReviewScore)
                          .ThenByDescending(c => c.IsGreatValue)
                          .ThenByDescending(c => c.PropertyID)
            }).ToList();

            // Count per category across the whole matched set, for the filter rail badges
            // (must be taken before paging trims the list to one page).
            var categoryCounts = cards
                .GroupBy(c => c.CategoryName ?? "")
                .ToDictionary(g => g.Key, g => g.Count());

            // Page the sorted list. The results column is fetched on its own (see the
            // AJAX search module in site.js) whenever a filter, the sort or the page
            // changes; that request carries the X-Requested-With header and gets just
            // the _Results partial back instead of the whole page.
            const int pageSize = 6;
            var totalResults = cards.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalResults / (double)pageSize));
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;
            var pageCards = cards.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var model = new HomeSearchViewModel
            {
                Location = location,
                MoveInDate = moveInDate,
                CategoryID = categoryID,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                Bedrooms = bedrooms,
                MinScore = minScore,
                AmenityIDs = amenityIDs,
                SortBy = string.IsNullOrWhiteSpace(sortBy) ? "recommended" : sortBy,
                Page = page,
                PageSize = pageSize,
                TotalResults = totalResults,
                Results = pageCards,
                Categories = await _context.PropertyCategories.ToListAsync(),
                Amenities = await _context.Amenities.OrderBy(a => a.AmenityName).ToListAsync(),
                PriceCeiling = await GetPriceCeilingAsync(),
                CategoryCounts = categoryCounts
            };

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_Results", model);
            }

            return View(model);
        }

        // Rounds the most expensive available listing up to a tidy step so the price slider
        // always covers the whole catalogue.
        private async Task<decimal> GetPriceCeilingAsync()
        {
            var highest = await _context.Properties
                .Where(p => p.IsAvailable)
                .Select(p => (decimal?)p.RentPrice)
                .MaxAsync() ?? 0;

            if (highest <= 0)
            {
                return 5000;
            }

            return Math.Ceiling(highest / 500m) * 500m;
        }

        // GET: /Home/SearchLocations?term=...
        public IActionResult SearchLocations(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Json(Array.Empty<string>());
            }

            var matches = MalaysiaLocations.All
                .Where(l => l.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l)
                .Take(10)
                .ToArray();

            return Json(matches);
        }

        // GET: /Home/PopularLocations
        // Cities that actually have listings, shown the moment the destination box is focused.
        public async Task<IActionResult> PopularLocations()
        {
            var cities = await _context.Properties
                .Where(p => p.IsAvailable)
                .GroupBy(p => p.City)
                .OrderByDescending(g => g.Count())
                .Select(g => new { City = g.Key, Count = g.Count() })
                .Take(6)
                .ToListAsync();

            return Json(cities);
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
