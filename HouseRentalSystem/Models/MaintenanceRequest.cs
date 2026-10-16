using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum MaintenanceStatus { Open, InProgress, Resolved }

    public class MaintenanceRequest
    {
        [Key]
        public int RequestID { get; set; }

        [ForeignKey("Property")]
        public int PropertyID { get; set; }
        public virtual Property Property { get; set; }

        [ForeignKey("Tenant")]
        public int TenantID { get; set; }
        public virtual User Tenant { get; set; }

        [Required, StringLength(150)]
        public string Title { get; set; }

        [Required, StringLength(1000)]
        public string Description { get; set; }

        [StringLength(250)]
        public string PhotoPath { get; set; }

        public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Open;

        public DateTime SubmittedDate { get; set; } = DateTime.Now;

        public DateTime? ResolvedDate { get; set; }

        // Set once an admin has reviewed the request and replied to the tenant. The owner's
        // report list only shows requests that have reached this point - see
        // MaintenanceController.OwnerRequests.
        [StringLength(1000)]
        public string? AdminReply { get; set; }

        public DateTime? RepliedDate { get; set; }

        [ForeignKey("RepliedByAdmin")]
        public int? RepliedByAdminID { get; set; }
        public virtual User? RepliedByAdmin { get; set; }
    }
}
