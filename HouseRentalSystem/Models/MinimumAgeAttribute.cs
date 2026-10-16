using System.ComponentModel.DataAnnotations;

namespace HouseRentalSystem.Models
{
    public class MinimumAgeAttribute : ValidationAttribute
    {
        private readonly int _minimumAge;

        public MinimumAgeAttribute(int minimumAge)
        {
            _minimumAge = minimumAge;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not DateTime dateOfBirth)
            {
                return ValidationResult.Success;
            }

            var today = DateTime.Today;
            var age = today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > today.AddYears(-age))
            {
                age--;
            }

            if (age < _minimumAge)
            {
                return new ValidationResult(ErrorMessage ?? $"You must be at least {_minimumAge} years old to register.");
            }

            return ValidationResult.Success;
        }
    }
}
