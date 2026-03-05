using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using kstech.Models;

namespace kstech.Models.Entities
{
    public class CartItem
    {
        [Key]
        public int CartItemID { get; set; }

        public string SessionId { get; set; } = string.Empty; // For guests

        public int? UserID { get; set; } // For logged in users
        [ForeignKey("UserID")]
        public User? User { get; set; }

        public int ProductID { get; set; }
        [ForeignKey("ProductID")]
        public Product? Product { get; set; }

        public int Quantity { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    }
}
