using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class BudgetEvent
    {
        [Key]
        public int BudgetEventID { get; set; }

        public int? OwnerUserID { get; set; }

        public int BudgetID { get; set; }

        [ForeignKey(nameof(BudgetID))]
        public FinancialBudget? Budget { get; set; }

        [MaxLength(30)]
        public string EventType { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10, 2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal? BeforeAmount { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal? AfterAmount { get; set; }

        [MaxLength(250)]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(30)]
        public string ReferenceType { get; set; } = string.Empty;

        [MaxLength(50)]
        public string ReferenceId { get; set; } = string.Empty;

        public int? PerformedByUserID { get; set; }

        [ForeignKey(nameof(PerformedByUserID))]
        public User? PerformedByUser { get; set; }

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    }
}
