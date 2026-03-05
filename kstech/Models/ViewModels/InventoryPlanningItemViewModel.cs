namespace kstech.Models.ViewModels
{
    public class InventoryPlanningItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public int UnitsSold { get; set; }
        public int OrdersCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal GrossProfit { get; set; }
        public int CurrentStock { get; set; }
        public decimal AverageDailyUnits { get; set; }
        public decimal DaysOfStockCover { get; set; }
        public string StockRiskLevel { get; set; } = "Healthy";
        public int RecommendedRestockUnits { get; set; }
        public decimal RecommendedRestockSpend { get; set; }
        public DateTime LastSoldAtUtc { get; set; }
    }
}
