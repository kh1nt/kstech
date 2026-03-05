namespace kstech.Models.ViewModels
{
    public class ReorderSuggestionViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public int ReorderLevel { get; set; }
        public int SuggestedOrderQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal EstimatedLineCost { get; set; }
        public string StockStatus { get; set; } = string.Empty;
    }

    public class SlowMoverSuggestionViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal RetailPrice { get; set; }
        public decimal StockValue { get; set; }
        public DateTime? LastSaleDateUtc { get; set; }
        public int DaysSinceLastSale { get; set; }
        public int RecommendedDiscountPercent { get; set; }
    }

    public class InventoryAutomationActionResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public int AffectedCount { get; set; }
    }
}
