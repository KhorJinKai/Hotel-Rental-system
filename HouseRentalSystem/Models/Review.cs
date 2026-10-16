using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public class Review
    {
        [Key]
        public int ReviewID { get; set; }

        [ForeignKey("Agreement")]
        public int AgreementID { get; set; }
        public virtual RentalAgreement Agreement { get; set; }

        [ForeignKey("Reviewer")]
        public int ReviewerID { get; set; }
        public virtual User Reviewer { get; set; }

        [ForeignKey("Reviewee")]
        public int RevieweeID { get; set; }
        public virtual User Reviewee { get; set; }

        [Required, Range(1, 5)]
        public int Rating { get; set; }

        [StringLength(500)]
        public string Comment { get; set; }

        public DateTime ReviewDate { get; set; } = DateTime.Now;
    }
}
