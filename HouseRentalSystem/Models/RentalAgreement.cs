using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum AgreementStatus
    {
        Active,
        Terminated,
        Completed,
        // Created as soon as the tenant accepts the agreement and pays the deposit, but the
        // tenancy isn't live yet - the owner and then an admin still have to approve the
        // underlying application. See RentalApplicationController.Create / AdminController.ApproveBooking.
        PendingApproval
    }

    public class RentalAgreement
    {
        [Key]
        public int AgreementID { get; set; }

        [ForeignKey("Application")]
        public int ApplicationID { get; set; }
        public virtual RentalApplication Application { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal MonthlyRent { get; set; }

        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal DepositAmount { get; set; }

        // What is left of the deposit after any arrears have been deducted from it.
        // Starts equal to DepositAmount and is only ever reduced by ArrearsService.
        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal DepositBalance { get; set; }

        public AgreementStatus Status { get; set; } = AgreementStatus.PendingApproval;

        public virtual ICollection<Payment> Payments { get; set; }
        public virtual ICollection<Review> Reviews { get; set; }
        public virtual ICollection<ArrearsNotice> ArrearsNotices { get; set; }
        public virtual ICollection<AgreementRequest> AgreementRequests { get; set; }
    }
}
