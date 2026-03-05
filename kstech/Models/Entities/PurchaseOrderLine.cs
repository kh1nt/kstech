using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class PurchaseOrderLine
    {
        [Key]
        public int PurchaseOrderLineID { get; set; }

        public int PurchaseOrderID { get; set; }

        [ForeignKey(nameof(PurchaseOrderID))]
        public PurchaseOrder? PurchaseOrder { get; set; }

        public int ProductID { get; set; }

        [ForeignKey(nameof(ProductID))]
        public Product? Product { get; set; }

        public int QuantityOrdered { get; set; }

        public int QuantityReceived { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal UnitCost { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal LineTotal { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
