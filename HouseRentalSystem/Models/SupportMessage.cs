using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum MessageSender { User, Admin, Bot }

    public class SupportMessage
    {
        [Key]
        public int MessageID { get; set; }

        [ForeignKey("Ticket")]
        public int TicketID { get; set; }
        public virtual SupportTicket Ticket { get; set; }

        public MessageSender Sender { get; set; }

        // Who actually typed it - null for the automated Bot messages.
        [ForeignKey("SentBy")]
        public int? SentByUserID { get; set; }
        public virtual User? SentBy { get; set; }

        [Required, StringLength(2000)]
        public string Body { get; set; }

        public DateTime SentDate { get; set; } = DateTime.Now;
    }
}
