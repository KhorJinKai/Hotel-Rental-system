using System.ComponentModel.DataAnnotations;

namespace HouseRentalSystem.Models
{
    public class Amenity
    {
        [Key]
        public int AmenityID { get; set; }

        [Required, StringLength(50)]
        public string AmenityName { get; set; }

        public virtual ICollection<PropertyAmenity> PropertyAmenities { get; set; }
    }
}
