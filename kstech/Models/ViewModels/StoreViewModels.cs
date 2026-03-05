using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class StoreHomeViewModel
    {
        public List<ProductViewModel> FeaturedProducts { get; set; } = new();
        public List<ProductViewModel> BestSellers { get; set; } = new();
        public List<StoreCategorySummaryViewModel> Categories { get; set; } = new();
        public List<TrendingGameViewModel> TrendingGames { get; set; } = new();
        public List<ProductViewModel> RecommendedProducts { get; set; } = new();
        public string BestSellerBasis { get; set; } = string.Empty;
        public string FeaturedBasis { get; set; } = string.Empty;
        public string RecommendationBasis { get; set; } = string.Empty;
    }

    public class StoreCatalogViewModel
    {
        public List<ProductViewModel> Products { get; set; } = new();
        public List<StoreCategorySummaryViewModel> Categories { get; set; } = new();
        public List<StoreBrandSummaryViewModel> Brands { get; set; } = new();

        public string SelectedCategory { get; set; } = "All";
        public string SelectedBrand { get; set; } = "All";
        public string SearchTerm { get; set; } = string.Empty;
        public string SortBy { get; set; } = "featured";
        public string? ActiveGameFilterName { get; set; }
        public int? ActiveGameSteamAppId { get; set; }
        public string? ActiveGamePcRequirementsMinHtml { get; set; }
        public string? ActiveGamePcRequirementsRecHtml { get; set; }
    }

    public class StoreCategorySummaryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class StoreBrandSummaryViewModel
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class TrendingGameViewModel
    {
        public int SteamAppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string BannerUrl { get; set; } = string.Empty;
        public int PlayerCount { get; set; }
        public List<string> Genres { get; set; } = new();
        public string? PcRequirementsMinHtml { get; set; }
        public string? PcRequirementsRecHtml { get; set; }
    }

    public class StoreGamesViewModel
    {
        public bool IsSteamConnected { get; set; }
        

        
        public List<TrendingGameViewModel> PersonalGames { get; set; } = new();
        public List<TrendingGameViewModel> Games { get; set; } = new();
    }
}
