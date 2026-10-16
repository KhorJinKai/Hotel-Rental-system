using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum ArrearsNoticeType
    {
        // 1st missed month: a payment reminder to the tenant.
        Reminder,
        // 2nd missed month: arrears cleared out of the security deposit.
        DepositDeduction,
        // 3rd missed month: a compound (late-payment penalty) charged to the tenant.
        Compound,
        // The Landlord has ended the tenancy after the compound went unpaid past its due date.
        Termination
    }

    // A dated record of every step the system took against a tenant who fell behind on rent.
    // Kept as an audit trail so both parties can see exactly what was issued and when.
    public class ArrearsNotice
    {
        [Key]
        public int ArrearsNoticeID { get; set; }

        [ForeignKey("Agreement")]
        public int AgreementID { get; set; }
        public virtual RentalAgreement Agreement { get; set; }

        // The overdue rent instalment that triggered this notice.
        [ForeignKey("Payment")]
        public int? PaymentID { get; set; }
        public virtual Payment Payment { get; set; }

        public ArrearsNoticeType NoticeType { get; set; }

        [Required]
        public DateTime IssuedDate { get; set; }

        // How many whole months the triggering rent was overdue when the notice was issued.
        public int MonthsOverdue { get; set; }

        // RM deducted (DepositDeduction) or charged (Compound). Zero for a reminder.
        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        [Required, StringLength(500)]
        public string Details { get; set; }

        // True once the tenant has opened the agreement page and seen the notice.
        public bool IsAcknowledged { get; set; }

        // When the notice was actually emailed to the tenant. Null until the send has
        // succeeded: the notice row is saved as soon as the ladder raises it, but the
        // email is a separate step that can fail (mail server down, bad credentials).
        // ArrearsService re-attempts every notice still null here on the next evaluation,
        // so a reminder is never silently lost the way it was when delivery relied on the
        // row simply existing.
        public DateTime? EmailedDate { get; set; }
    }
}
