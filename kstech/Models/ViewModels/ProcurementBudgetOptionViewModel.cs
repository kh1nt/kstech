namespace kstech.Models.ViewModels
{
    public class ProcurementBudgetOptionViewModel
    {
        public int BudgetId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal BudgetAmount { get; set; }
        public decimal CommittedAmount { get; set; }
        public decimal AvailableAmount { get; set; }
        public bool IsSuggested { get; set; }
    }
}
