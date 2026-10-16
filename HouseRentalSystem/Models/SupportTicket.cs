using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum TicketStatus { Open, Closed }

    public class SupportTicket
    {
        [Key]
        public int TicketID { get; set; }

        [ForeignKey("User")]
        public int UserID { get; set; }
        public virtual User User { get; set; }

        [Required, StringLength(150)]
        public string Subject { get; set; }

        public TicketStatus Status { get; set; } = TicketStatus.Open;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Bumped on every new message so the ticket list can sort by recent activity.
        public DateTime LastMessageDate { get; set; } = DateTime.Now;

        public virtual ICollection<SupportMessage> Messages { get; set; }
    }
}
