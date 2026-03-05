using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class InventoryMovement
    {
        [Key]
        public int MovementID { get; set; }

        public int ProductID { get; set; }

        [ForeignKey(nameof(ProductID))]
        public Product? Product { get; set; }

        public int? OwnerUserID { get; set; }

        [StringLength(20)]
        public string MovementType { get; set; } = "Adjustment"; // StockIn, StockOut, Adjustment

        public int QuantityDelta { get; set; }
        public int QuantityBefore { get; set; }
        public int QuantityAfter { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal UnitCostAtMovement { get; set; }

        [StringLength(100)]
        public string PartnerName { get; set; } = string.Empty;

        [StringLength(120)]
        public string Reason { get; set; } = string.Empty;

        [StringLength(30)]
        public string ReferenceType { get; set; } = string.Empty;

        [StringLength(50)]
        public string ReferenceId { get; set; } = string.Empty;

        public int? PerformedByUserID { get; set; }

        [ForeignKey(nameof(PerformedByUserID))]
        public User? PerformedByUser { get; set; }

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    }
}
