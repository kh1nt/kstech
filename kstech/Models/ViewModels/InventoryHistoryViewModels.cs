using System;
using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class InventoryHistoryIndexViewModel
    {
        public string Search { get; set; } = string.Empty;
        public string Type { get; set; } = "all";
        public int Days { get; set; } = 30;
        public string SelectedDateRange { get; set; } = "this_month";
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalMatched { get; set; }

        public List<InventoryHistoryTypeOptionViewModel> TypeOptions { get; set; } = new();
        public List<InventoryHistoryItemViewModel> Items { get; set; } = new();

        public int TotalPages => TotalMatched <= 0
            ? 1
            : (int)Math.Ceiling(TotalMatched / (double)Math.Max(1, PageSize));

        public int StartItem => TotalMatched == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int EndItem => TotalMatched == 0 ? 0 : Math.Min(Page * PageSize, TotalMatched);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    public class InventoryHistoryTypeOptionViewModel
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public class InventoryHistoryItemViewModel
    {
        public DateTime TimestampUtc { get; set; }
        public string RelativeTime { get; set; } = string.Empty;

        public string EventTypeKey { get; set; } = "system";
        public string EventTypeLabel { get; set; } = "System";

        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public int? QuantityDelta { get; set; }
        public decimal? UnitCost { get; set; }

        public string ActorDisplay { get; set; } = "System";
        public string Reference { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
