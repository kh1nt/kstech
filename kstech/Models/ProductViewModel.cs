using kstech.Models.ViewModels;

namespace kstech.Models
{
    public class ProductViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string NewCategory { get; set; } = string.Empty;
        public string NewBrand { get; set; } = string.Empty;
        public List<string> CategoryOptions { get; set; } = new List<string>();
        public List<string> BrandOptions { get; set; } = new List<string>();
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, string> KeySpecs { get; set; } = new Dictionary<string, string>();

        public int StockQuantity { get; set; }
        public int DamagedQuantity { get; set; }
        public int ReorderLevel { get; set; }
        public string StockStatus { get; set; } = string.Empty;
        public string ConditionStatus { get; set; } = "Good";
        public string ConditionNotes { get; set; } = string.Empty;
        public DateTime? LastConditionCheckUtc { get; set; }
        public int StockPercentage { get; set; }

        public decimal UnitCost { get; set; }
        public decimal RetailPrice { get; set; }
        public decimal? OriginalPrice { get; set; }

        public bool IsNewArrival { get; set; }
        public bool IsBestSeller { get; set; }
        public bool IsDeal { get; set; }
        public bool IsRefurbished { get; set; }
        public decimal? DiscountPercent { get; set; }
        public double FeaturedScore { get; set; }
        public string FeaturedReason { get; set; } = string.Empty;

        public decimal? EbayLivePrice { get; set; }
        public string MarketPriceSource { get; set; } = string.Empty;
        public DateTime? LastMarketPriceSyncUtc { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public List<string> GalleryImages { get; set; } = new List<string>();

        public double Rating { get; set; }
        public int ReviewCount { get; set; }

        public string Warranty { get; set; } = "3 Year Manufacturer Warranty";
        public string ReturnPolicy { get; set; } = "30-Day Free Returns";

        public int TotalSold { get; set; }
        public string SteamHeaderImageUrl { get; set; } = string.Empty;
        public List<string> SteamGenres { get; set; } = new List<string>();
        public string SteamShortDescription { get; set; } = string.Empty;
        public string? SteamPcRequirementsMinHtml { get; set; }
        public string? SteamPcRequirementsRecHtml { get; set; }
    }

    public class InventoryViewModel
    {
        public decimal TotalStockValue { get; set; }
        public double StockValueChangePercentage { get; set; }
        public int OutOfStockItems { get; set; }
        public int OutOfStockChange { get; set; }
        public int LowStockAlerts { get; set; }
        public bool ShowArchived { get; set; }
        public string SearchTerm { get; set; } = string.Empty;
        public string CategoryFilter { get; set; } = "All";
        public string StockStatusFilter { get; set; } = "All";
        public string SortBy { get; set; } = "name_asc";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalMatched { get; set; }
        public List<string> CategoryOptions { get; set; } = new List<string>();
        public List<string> BrandOptions { get; set; } = new List<string>();

        public List<ProductViewModel> Products { get; set; } = new List<ProductViewModel>();
        public List<SlowMoverSuggestionViewModel> SlowMoverSuggestions { get; set; } = new List<SlowMoverSuggestionViewModel>();
        public int TotalPages => TotalMatched <= 0
            ? 1
            : (int)Math.Ceiling(TotalMatched / (double)Math.Max(1, PageSize));
        public int StartItem => TotalMatched == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int EndItem => TotalMatched == 0 ? 0 : Math.Min(Page * PageSize, TotalMatched);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}
