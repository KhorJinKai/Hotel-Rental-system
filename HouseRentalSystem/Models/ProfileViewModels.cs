using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    public class UpdateProfileViewModel
    {
        [Required(ErrorMessage = "Please enter your full name.")]
        [StringLength(100)]
        public string FullName { get; set; }

        // Displayed but not editable here - changing the sign-in email is a bigger, separate
        // concern (would need re-verification), out of scope for this page.
        [ValidateNever]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter a phone number.")]
        [RegularExpression(@"^(\+?60|0)1[0-46-9]-?\d{7,8}$", ErrorMessage = "Enter a valid Malaysian phone number, e.g. 012-3456789.")]
        public string Phone { get; set; }

        [Required(ErrorMessage = "Please enter your date of birth.")]
        [DataType(DataType.Date)]
        [MinimumAge(18, ErrorMessage = "You must be at least 18 years old.")]
        public DateTime DateOfBirth { get; set; }

        [ValidateNever]
        public string? ProfilePhoto { get; set; }
    }

    // Changing a password while already signed in is confirmed by an emailed code rather
    // than the current password - see ProfileController.ChangePassword.
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Please enter the verification code.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Enter the 6-digit code from your email.")]
        public string Code { get; set; }

        [Required(ErrorMessage = "Please create a new password.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$",
            ErrorMessage = "Password must include an uppercase letter, a lowercase letter, a number, and a special character.")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your new password.")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
