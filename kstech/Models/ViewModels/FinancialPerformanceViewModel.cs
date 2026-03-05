namespace kstech.Models.ViewModels
{
    public class FinancialPerformanceViewModel
    {
        public string SelectedDateRange { get; set; } = "this_month";
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public string SelectedPaymentScope { get; set; } = "paid_only";

        public decimal Revenue { get; set; }
        public decimal CostOfGoodsSold { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal NetProfitMargin { get; set; }
        public decimal RevenueChangePercentage { get; set; }
        public decimal GrossProfitChangePercentage { get; set; }
        public decimal MarginChangePercentage { get; set; }
        public List<string> DailyProfitLabels { get; set; } = new();
        public List<decimal> DailyProfitValues { get; set; } = new();
        public List<CategoryProfitItemViewModel> ProfitByCategory { get; set; } = new();
        public List<FinancialTransactionViewModel> RecentTransactions { get; set; } = new();
        public int RecentTransactionsPage { get; set; } = 1;
        public int RecentTransactionsPageSize { get; set; } = 10;
        public int RecentTransactionsTotalCount { get; set; }
        public int RecentTransactionsTotalPages => RecentTransactionsTotalCount <= 0
            ? 1
            : (int)Math.Ceiling(RecentTransactionsTotalCount / (double)Math.Max(1, RecentTransactionsPageSize));
        public int RecentTransactionsStartItem => RecentTransactionsTotalCount == 0
            ? 0
            : ((RecentTransactionsPage - 1) * RecentTransactionsPageSize) + 1;
        public int RecentTransactionsEndItem => RecentTransactionsTotalCount == 0
            ? 0
            : Math.Min(RecentTransactionsPage * RecentTransactionsPageSize, RecentTransactionsTotalCount);
        public bool HasPreviousRecentTransactionsPage => RecentTransactionsPage > 1;
        public bool HasNextRecentTransactionsPage => RecentTransactionsPage < RecentTransactionsTotalPages;

        public decimal BudgetAmount { get; set; }
        public decimal BudgetVariance { get; set; }
        public decimal BudgetUtilizationPercentage { get; set; }
    }

    public class CategoryProfitItemViewModel
    {
        public string Category { get; set; } = string.Empty;
        public decimal Profit { get; set; }
        public decimal Percentage { get; set; }
    }

    public class FinancialTransactionViewModel
    {
        public string TransactionId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal SalePrice { get; set; }
        public decimal Cost { get; set; }
        public decimal Profit { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
    }
}
