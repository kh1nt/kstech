namespace kstech.Models.ViewModels
{
    public class SalesAnalyticsViewModel
    {
        public string SelectedDateRange { get; set; } = "this_month";
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public string SelectedPaymentFilter { get; set; } = "all";
        public string SelectedOrderStatusFilter { get; set; } = "all";

        public decimal TotalRevenue { get; set; }
        public int TotalOrders { get; set; }
        public decimal AverageOrderValue { get; set; }
        public int ActiveOrders { get; set; }
        public int PendingOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int PaidOrders { get; set; }
        public int RefundedOrders { get; set; }
        public decimal RevenueChangePercentage { get; set; }
        public decimal OrderChangePercentage { get; set; }
        public decimal PaidOrderRate { get; set; }
        public List<string> SalesTrendLabels { get; set; } = new();
        public List<decimal> SalesTrendValues { get; set; } = new();
        public List<SalesBreakdownItemViewModel> PaymentStatusBreakdown { get; set; } = new();
        public List<FastMovingItemViewModel> FastMovingItems { get; set; } = new();
        public List<SalesOrderSnapshotViewModel> RecentOrders { get; set; } = new();
        public int RecentSalesPage { get; set; } = 1;
        public int RecentSalesPageSize { get; set; } = 10;
        public int RecentSalesTotalCount { get; set; }
        public int RecentSalesTotalPages => RecentSalesTotalCount <= 0
            ? 1
            : (int)Math.Ceiling(RecentSalesTotalCount / (double)Math.Max(1, RecentSalesPageSize));
        public int RecentSalesStartItem => RecentSalesTotalCount == 0
            ? 0
            : ((RecentSalesPage - 1) * RecentSalesPageSize) + 1;
        public int RecentSalesEndItem => RecentSalesTotalCount == 0
            ? 0
            : Math.Min(RecentSalesPage * RecentSalesPageSize, RecentSalesTotalCount);
        public bool HasPreviousRecentSalesPage => RecentSalesPage > 1;
        public bool HasNextRecentSalesPage => RecentSalesPage < RecentSalesTotalPages;
    }

    public class SalesBreakdownItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class SalesOrderSnapshotViewModel
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string ProductsSummary { get; set; } = string.Empty;
    }

    public class FastMovingItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int UnitsSold { get; set; }
        public decimal Revenue { get; set; }
    }
}
