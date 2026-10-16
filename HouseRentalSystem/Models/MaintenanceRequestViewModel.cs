using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    // ViewModel for the tenant "report an issue" form (RentalAgreement/Details links here).
    // PropertyID is set server-side from the tenant's own active tenancy, never trusted
    // from the client beyond the hidden field round-trip.
    public class MaintenanceRequestViewModel
    {
        [Required]
        public int PropertyID { get; set; }

        [ValidateNever]
        public string PropertyTitle { get; set; }

        [Required(ErrorMessage = "Please give this issue a short title.")]
        [StringLength(150, ErrorMessage = "Title must be under 150 characters.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Please describe the issue.")]
        [StringLength(1000, MinimumLength = 10, ErrorMessage = "Description must be between 10 and 1000 characters.")]
        public string Description { get; set; }
    }
}
