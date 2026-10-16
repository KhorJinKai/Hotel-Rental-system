using System.ComponentModel.DataAnnotations;

namespace HouseRentalSystem.Models
{
    // ViewModel for the "Contact support" form (SupportController.Create).
    public class SupportTicketViewModel
    {
        [Required(ErrorMessage = "Please give this a short subject.")]
        [StringLength(150, ErrorMessage = "Subject must be under 150 characters.")]
        public string Subject { get; set; }

        [Required(ErrorMessage = "Please describe what you need help with.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Message must be between 10 and 2000 characters.")]
        public string Message { get; set; }
    }
}
