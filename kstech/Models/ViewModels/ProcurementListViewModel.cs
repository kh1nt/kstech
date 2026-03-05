using kstech.Models;

namespace kstech.Models.ViewModels
{
    public class ProcurementListViewModel
    {
        public List<ProcurementViewModel> Procurements { get; set; } = new();
        public List<ReorderSuggestionViewModel> ReorderSuggestions { get; set; } = new();
        public string SortBy { get; set; } = "date_desc";
        public string BudgetFilter { get; set; } = "all";
        public string StatusFilter { get; set; } = "all";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalMatched { get; set; }
        public int TotalPages => TotalMatched <= 0
            ? 1
            : (int)Math.Ceiling(TotalMatched / (double)Math.Max(1, PageSize));
        public int StartItem => TotalMatched == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int EndItem => TotalMatched == 0 ? 0 : Math.Min(Page * PageSize, TotalMatched);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
        public decimal ReorderSuggestedTotalCost => ReorderSuggestions.Sum(item => item.EstimatedLineCost);
        public int ReorderSuggestedTotalUnits => ReorderSuggestions.Sum(item => item.SuggestedOrderQuantity);
    }
}
