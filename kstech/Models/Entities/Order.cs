using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class Order
    {
        [Key]
        public int OrderID { get; set; }

        public int CustomerID { get; set; }
        [ForeignKey("CustomerID")]
        public Customer? Customer { get; set; }

        public int? OwnerUserID { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(10, 2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(20)]
        public string OrderStatus { get; set; } = "Pending";

        [StringLength(20)]
        public string PaymentStatus { get; set; } = "Pending";

        public int LoyaltyPointsEarned { get; set; }
        public int LoyaltyPointsRedeemed { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal LoyaltyDiscountAmount { get; set; }

        // Navigation Properties
        public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
