namespace kstech.Models.ViewModels
{
    public class BudgetPlanningViewModel
    {
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public DateTime SelectedMonthStartDateLocal { get; set; }
        public DateTime SelectedMonthEndDateLocal { get; set; }
        public int? SelectedBudgetId { get; set; }
        public string SelectedBudgetStatus { get; set; } = "Active";
        public bool ShowArchivedBudgets { get; set; }
        public string? BudgetSelectionMessage { get; set; }
        public string? SelectedHistoryMonthLabel { get; set; }

        // Core KPIs
        public decimal BudgetAmount { get; set; }
        public decimal ActualRevenue { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal BudgetVariance { get; set; }
        public decimal BudgetUtilizationPercentage { get; set; }
        public decimal BudgetGapToIntegratedTarget { get; set; }
        public decimal IntegratedBudgetTarget { get; set; }
        public decimal SuggestedRestockSpend { get; set; }
        public decimal ActualProcurementSpend { get; set; }
        public int AtRiskInventoryPlanningItems { get; set; }

        // Direct usage numbers (exposed separately for the gauge & KPI strip)
        public decimal ReservedAmount { get; set; }
        public decimal SpentAmount { get; set; }

        // Annual roll-up (sum of all active monthly budgets in the selected year)
        public decimal AnnualBudgetTotal { get; set; }
        public decimal AnnualSpentTotal { get; set; }
        public decimal AnnualReservedTotal { get; set; }
        public int AnnualBudgetMonthCount { get; set; }

        // Trend data
        public List<string> TrendLabels { get; set; } = new();
        public List<decimal> ActualRevenueTrendValues { get; set; } = new();
        public List<decimal> BudgetTargetTrendValues { get; set; } = new();

        // Budget table
        public List<BudgetMonthRowViewModel> MonthlyBudgets { get; set; } = new();

        // Category allocation breakdown
        public List<BudgetCategoryRowViewModel> CategoryAllocations { get; set; } = new();

        // Linked purchase orders for selected budget
        public List<BudgetLinkedPoViewModel> LinkedPurchaseOrders { get; set; } = new();

        // At-risk inventory
        public List<BudgetAllocationSuggestionViewModel> SuggestedAllocations { get; set; } = new();
        public List<BudgetAtRiskPartViewModel> AtRiskParts { get; set; } = new();

        // Full event feed for selected budget
        public List<BudgetHistoryItemViewModel> BudgetHistory { get; set; } = new();
    }

    public class BudgetMonthRowViewModel
    {
        public int BudgetId { get; set; }
        public DateTime MonthStartDateLocal { get; set; }
        public DateTime MonthEndDateLocal { get; set; }
        public decimal BudgetAmount { get; set; }
        public string Status { get; set; } = "Active";
        public string UsageStatus { get; set; } = "Unused";
        public decimal ReservedAmount { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal UtilizationPct { get; set; }
        public bool IsSelected { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<BudgetHistoryItemViewModel> History { get; set; } = new();
    }

    public class BudgetCategoryRowViewModel
    {
        public string Category { get; set; } = "Uncategorized";
        public decimal SuggestedAmount { get; set; }
        public decimal ReservedAmount { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal AllocationPct { get; set; }
        public decimal ReservedPct { get; set; }
        public decimal SpentPct { get; set; }
    }

    public class BudgetLinkedPoViewModel
    {
        public int PoId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class BudgetAllocationSuggestionViewModel
    {
        public string Category { get; set; } = "Uncategorized";
        public int UnitsSold { get; set; }
        public decimal RevenueSharePercentage { get; set; }
        public decimal SuggestedBudgetAmount { get; set; }
        public decimal SuggestedRestockSpend { get; set; }
    }

    public class BudgetHistoryItemViewModel
    {
        public int BudgetId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; }
        public string ChangedAtLabel { get; set; } = string.Empty;
    }

    public class BudgetAtRiskPartViewModel
    {
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public int CurrentStock { get; set; }
        public decimal AverageDailyUnits { get; set; }
        public decimal DaysOfStockCover { get; set; }
        public decimal SuggestedRestockSpend { get; set; }
    }
}
