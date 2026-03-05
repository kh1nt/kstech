using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class BusinessIntelligenceViewModel
    {
        public string SelectedDateRange { get; set; } = "this_month";
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public string SelectedAdjustmentFilter { get; set; } = "all";

        public List<CompetitorProductViewModel> CompetitorProducts { get; set; } = new List<CompetitorProductViewModel>();
        public int StockInMovementsInRange { get; set; }
        public int StockOutMovementsInRange { get; set; }
        public int OutstandingLoyaltyPoints { get; set; }
        public decimal LoyaltyLiability { get; set; }
    }

    public class CompetitorProductViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = "General"; // e.g. "GPUs", "Processors"
        public decimal StorePrice { get; set; }
        public decimal MarketAveragePrice { get; set; }
        public decimal CompetitorLowPrice { get; set; }
        public decimal CompetitorHighPrice { get; set; }
        public decimal SuggestedPrice { get; set; }
        public string SuggestedAdjustment { get; set; } = string.Empty; // e.g., "Increase by 5%", "Decrease by 2%", "Optimal"
        public string AdjustmentType { get; set; } = "Maintain"; // "Increase", "Decrease", "Maintain"
        public decimal EstimatedMonthlyUnits { get; set; }
        public decimal EstimatedRevenueImpact { get; set; }
    }
}
