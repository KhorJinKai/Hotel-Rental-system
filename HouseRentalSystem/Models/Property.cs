using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HouseRentalSystem.Models
{
    public class Property
    {
        [Key]
        public int PropertyID { get; set; }

        [ForeignKey("Owner")]
        public int OwnerID { get; set; }
        [ValidateNever]
        public virtual User Owner { get; set; }

        [ForeignKey("Category")]
        public int CategoryID { get; set; }
        [ValidateNever]
        public virtual PropertyCategory Category { get; set; }

        [Required, StringLength(150)]
        public string Title { get; set; }

        [Required, StringLength(1000)]
        public string Description { get; set; }

        [Required, StringLength(250)]
        public string Address { get; set; }

        [Required, StringLength(100)]
        public string City { get; set; }

        [Column(TypeName = "decimal(9,6)")]
        public decimal? Latitude { get; set; }

        [Column(TypeName = "decimal(9,6)")]
        public decimal? Longitude { get; set; }

        [Required, Column(TypeName = "decimal(10,2)")]
        [Range(0, 100000)]
        public decimal RentPrice { get; set; }

        [Required, Column(TypeName = "decimal(10,2)")]
        public decimal Deposit { get; set; }

        [Range(0, 20)]
        public int NumBedrooms { get; set; }

        [Range(0, 20)]
        public int NumBathrooms { get; set; }

        public bool IsAvailable { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation properties
        [ValidateNever]
        public virtual ICollection<PropertyPhoto> Photos { get; set; }
        [ValidateNever]
        public virtual ICollection<PropertyAmenity> PropertyAmenities { get; set; }
        [ValidateNever]
        public virtual ICollection<RentalApplication> RentalApplications { get; set; }
        [ValidateNever]
        public virtual ICollection<MaintenanceRequest> MaintenanceRequests { get; set; }
    }
}
