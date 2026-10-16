namespace HouseRentalSystem.Models
{
    // Populate this in PropertyController.Details(id) from Property + related entities.
    public class PropertyDetailsViewModel
    {
        public int PropertyID { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string CategoryName { get; set; }
        public decimal RentPrice { get; set; }
        public decimal Deposit { get; set; }
        public int NumBedrooms { get; set; }
        public int NumBathrooms { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public string OwnerName { get; set; }
        public DateTime OwnerJoinedDate { get; set; }
        public int OwnerListingCount { get; set; }
        public DateTime ListedDate { get; set; }
        public List<string> PhotoPaths { get; set; } = new();
        public List<string> AmenityNames { get; set; } = new();
        public List<ReviewSummary> Reviews { get; set; } = new();
        public bool HasExistingRequest { get; set; }
        public bool IsAvailable { get; set; }

        // Same 10 point presentation used on the search results cards.
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

        // How many tenants gave each star rating - drives the score breakdown bars.
        // Index 0 = 1 star ... index 4 = 5 stars.
        public int[] RatingDistribution { get; set; } = new int[5];

        public int PercentFor(int stars) =>
            ReviewCount == 0 ? 0 : (int)Math.Round(RatingDistribution[stars - 1] * 100.0 / ReviewCount);
    }

    public class ReviewSummary
    {
        public string ReviewerName { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
        public DateTime ReviewDate { get; set; }
    }
}
