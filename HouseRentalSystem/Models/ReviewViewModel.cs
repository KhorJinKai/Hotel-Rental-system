using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    // ViewModel for the tenant "leave a review" form (RentalAgreement/Details posts here).
    // Kept separate from the Review entity so AgreementID/ReviewerID/RevieweeID are set
    // server-side from the logged-in tenant's agreement, never trusted from the client.
    public class ReviewViewModel
    {
        [Required]
        public int AgreementID { get; set; }

        [ValidateNever]
        public string PropertyTitle { get; set; }
        [ValidateNever]
        public string OwnerName { get; set; }

        [Required(ErrorMessage = "Please select a star rating.")]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5 stars.")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Please write a short comment about your stay.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Comment must be between 10 and 500 characters.")]
        public string Comment { get; set; }
    }
}
