using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    // ViewModel for the booking/application form (Property Details "Request to book"
    // posts here). Keep validation attributes here rather than on the RentalApplication
    // entity itself, per assignment guidance to use view models with validation attributes.
    public class RentalApplicationViewModel
    {
        [Required]
        public int PropertyID { get; set; }

        [ValidateNever]
        public string PropertyTitle { get; set; }
        [ValidateNever]
        public string PropertyCity { get; set; }
        public decimal RentPrice { get; set; }
        public decimal Deposit { get; set; }
        [ValidateNever]
        public string PrimaryPhotoPath { get; set; }

        [Required(ErrorMessage = "Please select a move-in date.")]
        [DataType(DataType.Date)]
        [FutureDate(ErrorMessage = "Move-in date must be in the future.")]
        public DateTime MoveInDate { get; set; }

        [Required(ErrorMessage = "Please select a rental duration.")]
        [Range(6, 60, ErrorMessage = "Rental duration must be between 6 and 60 months.")]
        public int RentalDurationMonths { get; set; } = 6;

        [StringLength(500, ErrorMessage = "Message must be under 500 characters.")]
        public string? Message { get; set; }
    }

    // Custom validation attribute example (assignment §3.2 asks for custom validation
    // logic where necessary).
    public class FutureDateAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            if (value is DateTime date && date.Date <= DateTime.Now.Date)
            {
                return new ValidationResult(ErrorMessage ?? "Date must be in the future.");
            }
            return ValidationResult.Success;
        }
    }
}
