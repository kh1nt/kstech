using System.Collections.Generic;

namespace kstech.Models
{
    public class HomeDashboardViewModel
    {
        // Filter Properties
        public string FilterPeriod { get; set; } = "Monthly";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PeriodComparisonLabel { get; set; } = string.Empty;

        // Top Cards
        public decimal TotalSales { get; set; }
        public double SalesChangePercentage { get; set; }

        public decimal TotalProfits { get; set; }
        public double ProfitChangePercentage { get; set; }

        public int ActiveSkuCount { get; set; }
        public int AtRiskSkuCount { get; set; }
        public double StockAtRiskPercentage { get; set; }
        public double StockHealthPercentage { get; set; }
        public double StockHealthChangePercentage { get; set; }

        public int ActiveCustomers { get; set; }
        public int CustomerChangeCount { get; set; }

        // Charts
        public List<decimal> SalesChartData { get; set; } = new List<decimal>();
        public List<string> SalesChartLabels { get; set; } = new List<string>();

        public List<string> CategoryLabels { get; set; } = new List<string>();
        public List<int> CategoryData { get; set; } = new List<int>();

        // Fast Selling Parts
        public List<ProductViewModel> FastSellingProducts { get; set; } = new List<ProductViewModel>();

        // Recent Orders
        public List<RecentOrderViewModel> RecentOrders { get; set; } = new List<RecentOrderViewModel>();

        // Low Stock Alerts
        public int LowStockCount { get; set; }
        public List<string> LowStockSkus { get; set; } = new List<string>();

        // Action Items
        public int PendingOrdersCount { get; set; }
        public int OpenInquiriesCount { get; set; }
        public int CompletedOrdersCount { get; set; }
    }

    public class RecentOrderViewModel
    {
        public string OrderId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class SalesDataPoint
    {
        public string Month { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
