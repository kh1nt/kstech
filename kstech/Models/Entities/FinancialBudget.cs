using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class FinancialBudget
    {
        [Key]
        public int BudgetID { get; set; }

        public int? OwnerUserID { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime PeriodStartDateLocal { get; set; }

        public DateTime PeriodEndDateLocal { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal BudgetAmount { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
