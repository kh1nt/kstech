using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }

        public int OrderID { get; set; }
        [ForeignKey("OrderID")]
        public Order? Order { get; set; }

        public int? OwnerUserID { get; set; }

        [StringLength(25)]
        public string PaymentMethod { get; set; } = string.Empty;

        public DateTime PaymentDateUtc { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(10, 2)")]
        public decimal AmountPaid { get; set; }
    }
}
