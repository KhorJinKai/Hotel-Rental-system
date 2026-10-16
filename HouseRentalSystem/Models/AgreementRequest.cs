using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum RequestType { Cancellation, Refund }
    public enum RequestStatus { Pending, Approved, Rejected }

    // A tenant or owner asking an admin to approve ending a tenancy early, or refunding
    // money (usually the deposit) out of it. Kept as one table since both are "a request
    // against an agreement that needs an admin decision" - RequestedAmount/ApprovedAmount
    // are only meaningful for Refund and stay null for Cancellation.
    public class AgreementRequest
    {
        [Key]
        public int RequestID { get; set; }

        [ForeignKey("Agreement")]
        public int AgreementID { get; set; }
        public virtual RentalAgreement Agreement { get; set; }

        [ForeignKey("RequestedBy")]
        public int RequestedByUserID { get; set; }
        public virtual User RequestedBy { get; set; }

        public RequestType RequestType { get; set; }

        [Required, StringLength(500)]
        public string Reason { get; set; }

        // What the requester asked for (Refund only).
        [Column(TypeName = "decimal(10,2)")]
        public decimal? RequestedAmount { get; set; }

        // What the admin actually approved (Refund only) - may differ from RequestedAmount,
        // e.g. a partial refund after deducting for damage.
        [Column(TypeName = "decimal(10,2)")]
        public decimal? ApprovedAmount { get; set; }

        public RequestStatus Status { get; set; } = RequestStatus.Pending;

        [StringLength(500)]
        public string? AdminNote { get; set; }

        public DateTime SubmittedDate { get; set; } = DateTime.Now;

        public DateTime? DecidedDate { get; set; }

        [ForeignKey("DecidedByAdmin")]
        public int? DecidedByAdminID { get; set; }
        public virtual User? DecidedByAdmin { get; set; }

        // Optimistic-concurrency token - stops two admins deciding the same request at once
        // (both would otherwise record a decision, and a Refund would be paid out twice).
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
