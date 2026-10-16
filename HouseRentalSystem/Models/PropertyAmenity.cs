using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    // Join entity for the many-to-many relationship between Property and Amenity
    public class PropertyAmenity
    {
        [ForeignKey("Property")]
        public int PropertyID { get; set; }
        public virtual Property Property { get; set; }

        [ForeignKey("Amenity")]
        public int AmenityID { get; set; }
        public virtual Amenity Amenity { get; set; }
    }
}
