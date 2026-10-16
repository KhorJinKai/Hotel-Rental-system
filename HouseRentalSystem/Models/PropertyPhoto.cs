using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public class PropertyPhoto
    {
        [Key]
        public int PhotoID { get; set; }

        [ForeignKey("Property")]
        public int PropertyID { get; set; }
        public virtual Property Property { get; set; }

        [Required, StringLength(250)]
        public string PhotoPath { get; set; }

        public bool IsPrimary { get; set; }
    }
}
