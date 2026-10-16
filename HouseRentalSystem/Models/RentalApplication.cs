using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum ApplicationStatus
    {
        // Numeric order matches storage order in the database - new values are appended,
        // never inserted between existing ones, or every already-stored row silently
        // reinterprets as the wrong status.
        Pending,
        Approved,
        Rejected,
        // Owner has approved; the property is reserved (IsAvailable = false) but the
        // agreement and payment schedule aren't created until an admin signs off too.
        PendingAdminApproval,
        // Tenant has submitted the application but hasn't yet accepted the tenancy agreement
        // and paid the security deposit - not visible to the owner until that happens.
        AwaitingPayment
    }

    public class RentalApplication
    {
        [Key]
        public int ApplicationID { get; set; }

        [ForeignKey("Property")]
        public int PropertyID { get; set; }
        public virtual Property Property { get; set; }

        [ForeignKey("Tenant")]
        public int TenantID { get; set; }
        public virtual User Tenant { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime MoveInDate { get; set; }

        [Range(6, 60)]
        public int RentalDurationMonths { get; set; } = 6;

        public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

        [StringLength(500)]
        public string Message { get; set; }

        [StringLength(500)]
        public string? RejectionReason { get; set; }

        public DateTime AppliedDate { get; set; } = DateTime.Now;

        // Set when the tenant ticks "I agree" on the tenancy agreement, before they're taken
        // to pay the deposit. Null until then - see RentalApplicationController.AcceptTerms.
        public DateTime? TermsAcceptedDate { get; set; }

        // Optimistic-concurrency token. The owner-approve, admin-approve and reject actions
        // all gate on Status; this makes two of them running at once (a double-click, or two
        // admins) fail the second save instead of both processing the same application.
        [Timestamp]
        public byte[]? RowVersion { get; set; }

        public virtual RentalAgreement Agreement { get; set; }
    }
}
