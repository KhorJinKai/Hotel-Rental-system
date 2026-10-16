using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    // ViewModel for the tenant/owner "request cancellation / refund" form
    // (RentalAgreement/Details links here).
    public class AgreementRequestViewModel
    {
        [Required]
        public int AgreementID { get; set; }

        [Required]
        public RequestType RequestType { get; set; }

        [Required(ErrorMessage = "Please explain your request.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Reason must be between 10 and 500 characters.")]
        public string Reason { get; set; }

        // Refund only - left null and ignored server-side for Cancellation.
        [Range(0.01, 100000, ErrorMessage = "Enter an amount greater than RM 0.")]
        public decimal? RequestedAmount { get; set; }

        [ValidateNever]
        public string PropertyTitle { get; set; }

        [ValidateNever]
        public decimal DepositBalance { get; set; }
    }
}
