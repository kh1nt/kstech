using System.Collections.Generic;

namespace kstech.Models
{
    public class InventoryDashboardViewModel
    {
        public string SelectedDateRange { get; set; } = "this_month";
        public DateTime FilterStartDate { get; set; }
        public DateTime FilterEndDate { get; set; }
        public decimal TotalInventoryValue { get; set; }
        public double InventoryValueChange { get; set; } // Percentage change vs last month
        public int LowStockCount { get; set; }
        public List<string> LowStockSkus { get; set; } = new List<string>(); // For the "12 SKUs" detail
        public int DamagedItemCount { get; set; }
        public int DamagedUnitCount { get; set; }
        public decimal EstimatedDamageLossValue { get; set; }
        public double DamageRate { get; set; }
        public List<string> DamagedSkus { get; set; } = new List<string>();
        public decimal MonthlySales { get; set; }
        public double MonthlySalesChange { get; set; } // Percentage change
        public string HighestSalesCategory { get; set; } = string.Empty;
        public double AvgMarketMarkup { get; set; }
        public double MarkupChange { get; set; } // Percentage change
        public int StockInUnitsThisMonth { get; set; }
        public int StockOutUnitsThisMonth { get; set; }

        // Charts Data
        public List<PriceFluctuationDataPoint> PriceFluctuationData { get; set; } = new List<PriceFluctuationDataPoint>();
        public List<StockMovementDataPoint> StockMovementData { get; set; } = new List<StockMovementDataPoint>();
        public List<StockDistributionDataPoint> StockDistributionData { get; set; } = new List<StockDistributionDataPoint>();
    }

    public class PriceFluctuationDataPoint
    {
        public string Month { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string Tooltip { get; set; } = string.Empty; // e.g. "NVIDIA RTX 4080 Peak: ₱1,250"
    }

    public class StockDistributionDataPoint
    {
        public string Category { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public decimal Value { get; set; }
        public string Color { get; set; } = string.Empty; // Hex or Tailwind class if we map it
    }

    public class StockMovementDataPoint
    {
        public string Month { get; set; } = string.Empty;
        public int StockInUnits { get; set; }
        public int StockOutUnits { get; set; }
        public int NetUnits => StockInUnits - StockOutUnits;
    }
}
