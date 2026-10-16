using System.ComponentModel.DataAnnotations;

namespace HouseRentalSystem.Models
{
    public class PropertyCategory
    {
        [Key]
        public int CategoryID { get; set; }

        [Required, StringLength(50)]
        public string CategoryName { get; set; }

        public virtual ICollection<Property> Properties { get; set; }
    }
}
