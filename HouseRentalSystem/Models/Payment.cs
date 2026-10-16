using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum PaymentType { Rent, Deposit, Compound, Refund }
    public enum PaymentStatus { Paid, Pending, Overdue }

    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }

        [ForeignKey("Agreement")]
        public int AgreementID { get; set; }
        public virtual RentalAgreement Agreement { get; set; }

        // When the money is owed. Fixed when the schedule is generated and never changed -
        // arrears are measured from this date, so it must not move when the tenant pays.
        [Required, DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        // When the money was actually received. Null until then.
        [DataType(DataType.Date)]
        public DateTime? PaymentDate { get; set; }

        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        public PaymentType PaymentType { get; set; }

        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

        // Set when arrears were cleared out of the security deposit rather than paid by the
        // tenant (see ArrearsService). The payment still counts as settled for the owner.
        public bool SettledFromDeposit { get; set; }

        // Which month of the tenancy this rent covers (1 = first month). 0 for deposit/compound.
        public int RentMonthNumber { get; set; }

        [StringLength(250)]
        public string ReceiptPath { get; set; }
    }
}
