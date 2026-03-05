using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class PurchaseOrder
    {
        [Key]
        public int PurchaseOrderID { get; set; }

        public int? OwnerUserID { get; set; }

        public int? BudgetID { get; set; }

        [ForeignKey(nameof(BudgetID))]
        public FinancialBudget? Budget { get; set; }

        [StringLength(30)]
        public string PurchaseOrderNumber { get; set; } = string.Empty;

        [StringLength(100)]
        public string SupplierName { get; set; } = string.Empty;

        [StringLength(20)]
        public string Status { get; set; } = "Draft";

        [Column(TypeName = "decimal(10, 2)")]
        public decimal TotalAmount { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovedAtUtc { get; set; }

        public DateTime? FullyReceivedAtUtc { get; set; }

        [StringLength(500)]
        public string Notes { get; set; } = string.Empty;

        public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    }
}
