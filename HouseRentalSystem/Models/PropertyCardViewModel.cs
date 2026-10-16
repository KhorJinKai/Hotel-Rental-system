namespace HouseRentalSystem.Models
{
    // Lightweight shape used for the search results grid on the homepage.
    // Populate this from Property + related PropertyPhoto/Review data in the controller.
    public class PropertyCardViewModel
    {
        public int PropertyID { get; set; }
        public string Title { get; set; }
        public string City { get; set; }
        public string Address { get; set; }
        public string CategoryName { get; set; }
        public string PrimaryPhotoPath { get; set; }
        public decimal RentPrice { get; set; }
        public decimal Deposit { get; set; }
        public int NumBedrooms { get; set; }
        public int NumBathrooms { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public bool IsNewListing { get; set; }
        public bool IsGreatValue { get; set; }
        public List<string> AmenityNames { get; set; } = new();

        // Booking-site style review score: the 1-5 star average presented on a 10 point scale
        // (8.6 reads more precisely than 4.3 stars) plus the word travellers actually look for.
        public double ReviewScore => Math.Round(AverageRating * 2, 1);
        public string ScoreWord => ReviewCount == 0 ? "No reviews yet" : ReviewScore switch
        {
            >= 9.0 => "Exceptional",
            >= 8.0 => "Excellent",
            >= 7.0 => "Very good",
            >= 6.0 => "Good",
            >= 5.0 => "Pleasant",
            _ => "Review score"
        };

        // Shown as a struck-through "was" price next to great-value listings so the saving is visible.
        public decimal ComparablePrice { get; set; }
        public int DiscountPercent =>
            ComparablePrice > RentPrice && RentPrice > 0
                ? (int)Math.Round((ComparablePrice - RentPrice) / ComparablePrice * 100)
                : 0;
    }

    public class HomeSearchViewModel
    {
        // ---- Search bar ----
        public string Location { get; set; }
        public DateTime? MoveInDate { get; set; }
        public int? CategoryID { get; set; }

        // ---- Filter rail ----
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public int? Bedrooms { get; set; }
        public int? MinScore { get; set; }
        public int[] AmenityIDs { get; set; } = Array.Empty<int>();

        // ---- Sorting ----
        public string SortBy { get; set; } = "recommended";

        // ---- Paging ----
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 6;

        // Full count of matched listings (Results only holds the current page's slice).
        public int TotalResults { get; set; }
        public int TotalPages => PageSize > 0
            ? Math.Max(1, (int)Math.Ceiling(TotalResults / (double)PageSize))
            : 1;

        // ---- Data for the page ----
        public List<PropertyCardViewModel> Results { get; set; } = new();
        public List<PropertyCategory> Categories { get; set; } = new();
        public List<Amenity> Amenities { get; set; } = new();
        public decimal PriceCeiling { get; set; } = 5000;

        // Listings per category across the whole matched set (not just the current page),
        // for the counts shown next to each type in the filter rail.
        public Dictionary<string, int> CategoryCounts { get; set; } = new();

        public bool HasActiveFilters =>
            MinPrice.HasValue || MaxPrice.HasValue || Bedrooms.HasValue ||
            MinScore.HasValue || (AmenityIDs != null && AmenityIDs.Length > 0) ||
            CategoryID.HasValue || !string.IsNullOrWhiteSpace(Location);
    }
}
