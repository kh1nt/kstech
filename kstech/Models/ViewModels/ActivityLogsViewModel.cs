using System;
using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class ActivityLogIndexViewModel
    {
        public string Search { get; set; } = string.Empty;
        public string Role { get; set; } = "All";
        public int Days { get; set; } = 7;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        public int TotalMatched { get; set; }
        public int OwnerEvents { get; set; }
        public int SuperAdminEvents { get; set; }
        public int InventoryManagerEvents { get; set; }
        public int SalesStaffEvents { get; set; }

        public List<string> Roles { get; set; } = new List<string>();
        public List<ActivityLogItemViewModel> Logs { get; set; } = new List<ActivityLogItemViewModel>();

        public int TotalPages => TotalMatched <= 0
            ? 1
            : (int)Math.Ceiling(TotalMatched / (double)Math.Max(1, PageSize));

        public int StartItem => TotalMatched == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int EndItem => TotalMatched == 0 ? 0 : Math.Min(Page * PageSize, TotalMatched);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    public class ActivityLogItemViewModel
    {
        public int LogId { get; set; }
        public DateTime Timestamp { get; set; }
        public string RelativeTime { get; set; } = string.Empty;
        public string UserRole { get; set; } = "System";
        public string UserDisplay { get; set; } = "System";
        public string Action { get; set; } = string.Empty;
    }
}
