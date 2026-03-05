using System.Diagnostics;
using kstech.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using kstech.Utilities;
using kstech.Configuration;
using Microsoft.EntityFrameworkCore;
using kstech.Models.Entities;
using Microsoft.Extensions.Options;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin,Owner")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly kstech.Services.IInventoryService _inventoryService;
        private readonly kstech.Services.IBusinessIntelligenceService _biService;
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly kstech.Services.ITenantContext _tenantContext;
        private readonly kstech.Services.IReportPdfService _reportPdfService;
        private readonly kstech.Services.IReportCloudArchiveService _reportCloudArchiveService;
        private readonly int _lowStockThreshold;

        public HomeController(ILogger<HomeController> logger,
            kstech.Services.IInventoryService inventoryService,
            kstech.Services.IBusinessIntelligenceService biService,
            kstech.Data.ApplicationDbContext context,
            kstech.Services.ITenantContext tenantContext,
            kstech.Services.IReportPdfService reportPdfService,
            kstech.Services.IReportCloudArchiveService reportCloudArchiveService,
            IOptions<InventoryRuleOptions> inventoryRuleOptions)
        {
            _logger = logger;
            _inventoryService = inventoryService;
            _biService = biService;
            _context = context;
            _tenantContext = tenantContext;
            _reportPdfService = reportPdfService;
            _reportCloudArchiveService = reportCloudArchiveService;
            _lowStockThreshold = Math.Max(1, inventoryRuleOptions.Value.LowStockThreshold);
        }

        // Action of Index
        public IActionResult Index(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string? filterPeriod = null,
            DateTime? customStart = null,
            DateTime? customEnd = null)
        {
            if (_tenantContext.IsSuperAdmin && !_tenantContext.HasOwnerScope)
            {
                return RedirectToAction("Index", "Owner");
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var resolvedRange = !string.IsNullOrWhiteSpace(filterPeriod)
                ? MapLegacyFilterToRange(filterPeriod)
                : range;
            var normalizedRange = NormalizeDateRange(resolvedRange);

            var effectiveStartDate = startDate ?? customStart;
            var effectiveEndDate = endDate ?? customEnd;
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(
                normalizedRange,
                effectiveStartDate,
                effectiveEndDate);

            var rangeStartLocalDate = resolvedStartDate.Date;
            var rangeEndLocalDate = resolvedEndDate.Date;

            var rangeStartUtc = ConvertLocalDateStartToUtc(rangeStartLocalDate);
            var rangeEndUtc = ConvertLocalDateEndToUtc(rangeEndLocalDate);
            if (rangeEndUtc < rangeStartUtc)
            {
                rangeEndUtc = rangeStartUtc.AddDays(1).AddTicks(-1);
            }

            // CALC-KPI: Build a previous period window with the same duration for change/variance comparisons.
            var previousRangeEnd = rangeStartUtc.AddTicks(-1);
            var previousRangeStart = previousRangeEnd - (rangeEndUtc - rangeStartUtc);

            var inventoryStats = _inventoryService.GetDashboardStats(rangeStartLocalDate, rangeEndLocalDate);
            // CALC-KPI: Sales KPI and period-over-period sales change for the dashboard summary cards.
            var filteredTotalSales = _inventoryService.GetTotalSales(rangeStartUtc, rangeEndUtc);
            var previousTotalSales = _inventoryService.GetTotalSales(previousRangeStart, previousRangeEnd);
            var salesChange = CalculatePercentageChange(filteredTotalSales, previousTotalSales);

            // CALC-KPI: Estimate COGS from order details to derive profit without a separate financial snapshot table.
            var currentCogs = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od => 
                    od.OrderID != 0 &&
                    od.Order != null &&
                    od.Order.OrderStatus != "Cancelled" &&
                    od.Order.PaymentStatus == "Paid" &&
                    od.Order.OrderDate >= rangeStartUtc &&
                    od.Order.OrderDate <= rangeEndUtc &&
                    (!applyOwnerFilter || od.Order.OwnerUserID == ownerUserId) &&
                    od.Product != null)
                .Sum(od => od.Quantity * od.Product!.CostPrice);

            var previousCogs = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od => 
                    od.OrderID != 0 &&
                    od.Order != null &&
                    od.Order.OrderStatus != "Cancelled" &&
                    od.Order.PaymentStatus == "Paid" &&
                    od.Order.OrderDate >= previousRangeStart &&
                    od.Order.OrderDate <= previousRangeEnd &&
                    (!applyOwnerFilter || od.Order.OwnerUserID == ownerUserId) &&
                    od.Product != null)
                .Sum(od => od.Quantity * od.Product!.CostPrice);

            // CALC-KPI: Profit KPI is estimated as sales minus COGS for current and previous periods.
            var estimatedProfit = filteredTotalSales - currentCogs;
            var previousEstimatedProfit = previousTotalSales - previousCogs;
            var profitChange = CalculatePercentageChange(estimatedProfit, previousEstimatedProfit);

            var pendingOrders = _context.Orders.Count(o =>
                (o.OrderStatus == "Pending" || o.OrderStatus == "Processing") &&
                (!applyOwnerFilter || o.OwnerUserID == ownerUserId));

            var openInquiries = _context.TechnicalInquiries.Count(t =>
                !t.IsResolved &&
                (!applyOwnerFilter || t.OwnerUserID == ownerUserId));

            var completedOrders = _context.Orders.Count(o =>
                (o.OrderStatus == "Completed" || o.OrderStatus == "Paid") &&
                (!applyOwnerFilter || o.OwnerUserID == ownerUserId));

            // CALC-KPI: Active customers are distinct buyers in the selected period vs. the previous period.
            var activeCustomers = _context.Orders
                .Where(o =>
                    o.OrderDate >= rangeStartUtc &&
                    o.OrderDate <= rangeEndUtc &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();

            var previousActiveCustomers = _context.Orders
                .Where(o =>
                    o.OrderDate >= previousRangeStart &&
                    o.OrderDate <= previousRangeEnd &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();
            var customerChange = CalculatePercentageChange(activeCustomers, previousActiveCustomers);

            var activeProducts = _context.Products
                .Where(p => p.MarketPriceSource != "Archived")
                .Where(p => !applyOwnerFilter || p.OwnerUserID == ownerUserId)
                .Select(p => new { p.ProductID, p.StockQuantity })
                .ToList();

            var periodMovements = _context.InventoryMovements
                .Where(movement =>
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtc &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();

            // CALC-KPI: Stock health uses at-risk SKU share: (Out of Stock + Low Stock) / Active SKUs.
            var activeSkuCount = activeProducts.Count;
            var atRiskSkuCount = activeProducts.Count(product => product.StockQuantity <= _lowStockThreshold);
            var currentAtRiskPercentage = CalculateRatioPercentage(atRiskSkuCount, activeSkuCount);

            var movementSummaryByProduct = periodMovements
                .GroupBy(movement => movement.ProductID)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        StockIn = group
                            .Where(movement => movement.MovementType == "StockIn")
                            .Sum(movement => Math.Max(0, movement.QuantityDelta)),
                        StockOut = group
                            .Where(movement => movement.MovementType == "StockOut")
                            .Sum(movement => Math.Abs(movement.QuantityDelta))
                    });

            var estimatedStartingAtRiskSkuCount = activeProducts.Count(product =>
            {
                if (!movementSummaryByProduct.TryGetValue(product.ProductID, out var movementSummary))
                {
                    return product.StockQuantity <= _lowStockThreshold;
                }

                var estimatedStartingStock = product.StockQuantity - movementSummary.StockIn + movementSummary.StockOut;
                return estimatedStartingStock <= _lowStockThreshold;
            });

            var estimatedStartingAtRiskPercentage = CalculateRatioPercentage(estimatedStartingAtRiskSkuCount, activeSkuCount);
            var stockHealthPercentage = activeSkuCount > 0
                ? Math.Round(100d - currentAtRiskPercentage, 2)
                : 0d;
            var estimatedStartingHealthPercentage = activeSkuCount > 0
                ? Math.Round(100d - estimatedStartingAtRiskPercentage, 2)
                : 0d;
            var stockHealthChangePercentage = Math.Round(stockHealthPercentage - estimatedStartingHealthPercentage, 2);

            var salesHistory = _inventoryService.GetSalesChartData(rangeStartUtc, rangeEndUtc);
            var categorySales = _inventoryService.GetSalesByCategory(rangeStartUtc, rangeEndUtc, out var categoryLabels);
            // CALC-KPI: Convert category share values into rounded integer percentages for the chart UI.
            var categoryPercentages = categorySales
                .Select(amount => (int)Math.Round(amount, MidpointRounding.AwayFromZero))
                .ToList();

            var model = new HomeDashboardViewModel
            {
                FilterPeriod = normalizedRange,
                StartDate = rangeStartLocalDate,
                EndDate = rangeEndLocalDate,

                TotalSales = filteredTotalSales,
                SalesChangePercentage = salesChange,

                TotalProfits = estimatedProfit,
                ProfitChangePercentage = profitChange,

                ActiveSkuCount = activeSkuCount,
                AtRiskSkuCount = atRiskSkuCount,
                StockAtRiskPercentage = currentAtRiskPercentage,
                StockHealthPercentage = stockHealthPercentage,
                StockHealthChangePercentage = stockHealthChangePercentage,

                ActiveCustomers = activeCustomers,
                CustomerChangePercentage = customerChange,

                SalesChartData = salesHistory.Select(s => s.Amount).ToList(),
                SalesChartLabels = salesHistory.Select(s => s.Month).ToList(),

                CategoryLabels = categoryLabels,
                CategoryData = categoryPercentages,

                FastSellingProducts = _inventoryService.GetTopSellingProducts(4, rangeStartUtc, rangeEndUtc),
                RecentOrders = _inventoryService.GetRecentOrders(5, rangeStartUtc, rangeEndUtc),

                LowStockCount = inventoryStats.LowStockCount,
                LowStockSkus = inventoryStats.LowStockSkus,

                PendingOrdersCount = pendingOrders,
                OpenInquiriesCount = openInquiries,
                CompletedOrdersCount = completedOrders
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult ExportDashboardReportPdf(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month")
        {
            if (_tenantContext.IsSuperAdmin && !_tenantContext.HasOwnerScope)
            {
                return RedirectToAction("Index", "Owner");
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            var normalizedRange = NormalizeDateRange(range);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var reportModel = BuildHomeDashboardModel(
                normalizedRange,
                resolvedStartDate.Date,
                resolvedEndDate.Date,
                applyOwnerFilter,
                ownerUserId);

            var reportBytes = _reportPdfService.BuildHomeDashboardReport(reportModel);
            var fileName = $"dashboard-report-{reportModel.StartDate:yyyyMMdd}-{reportModel.EndDate:yyyyMMdd}.pdf";

            var archiveResult = _reportCloudArchiveService.TryUploadReport(
                reportType: "dashboard",
                fileName: fileName,
                reportBytes: reportBytes,
                ownerUserId: _tenantContext.OwnerUserId,
                periodStartLocal: reportModel.StartDate,
                periodEndLocal: reportModel.EndDate);
            if (!archiveResult.Uploaded)
            {
                _logger.LogWarning("Dashboard report upload skipped/failed: {Message}", archiveResult.Message);
            }

            return File(reportBytes, "application/pdf", fileName);
        }

        private HomeDashboardViewModel BuildHomeDashboardModel(
            string normalizedRange,
            DateTime rangeStartLocalDate,
            DateTime rangeEndLocalDate,
            bool applyOwnerFilter,
            int ownerUserId)
        {
            var rangeStartUtc = ConvertLocalDateStartToUtc(rangeStartLocalDate);
            var rangeEndUtc = ConvertLocalDateEndToUtc(rangeEndLocalDate);
            if (rangeEndUtc < rangeStartUtc)
            {
                rangeEndUtc = rangeStartUtc.AddDays(1).AddTicks(-1);
            }

            // CALC-KPI: Build a previous period window with the same duration for change/variance comparisons.
            var previousRangeEnd = rangeStartUtc.AddTicks(-1);
            var previousRangeStart = previousRangeEnd - (rangeEndUtc - rangeStartUtc);

            var inventoryStats = _inventoryService.GetDashboardStats(rangeStartLocalDate, rangeEndLocalDate);
            // CALC-KPI: Sales KPI and period-over-period sales change for the dashboard summary cards.
            var filteredTotalSales = _inventoryService.GetTotalSales(rangeStartUtc, rangeEndUtc);
            var previousTotalSales = _inventoryService.GetTotalSales(previousRangeStart, previousRangeEnd);
            var salesChange = CalculatePercentageChange(filteredTotalSales, previousTotalSales);

            // CALC-KPI: Estimate COGS from order details to derive profit without a separate financial snapshot table.
            var currentCogs = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od =>
                    od.OrderID != 0 &&
                    od.Order != null &&
                    od.Order.OrderStatus != "Cancelled" &&
                    od.Order.PaymentStatus == "Paid" &&
                    od.Order.OrderDate >= rangeStartUtc &&
                    od.Order.OrderDate <= rangeEndUtc &&
                    (!applyOwnerFilter || od.Order.OwnerUserID == ownerUserId) &&
                    od.Product != null)
                .Sum(od => od.Quantity * od.Product!.CostPrice);

            var previousCogs = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od =>
                    od.OrderID != 0 &&
                    od.Order != null &&
                    od.Order.OrderStatus != "Cancelled" &&
                    od.Order.PaymentStatus == "Paid" &&
                    od.Order.OrderDate >= previousRangeStart &&
                    od.Order.OrderDate <= previousRangeEnd &&
                    (!applyOwnerFilter || od.Order.OwnerUserID == ownerUserId) &&
                    od.Product != null)
                .Sum(od => od.Quantity * od.Product!.CostPrice);

            // CALC-KPI: Profit KPI is estimated as sales minus COGS for current and previous periods.
            var estimatedProfit = filteredTotalSales - currentCogs;
            var previousEstimatedProfit = previousTotalSales - previousCogs;
            var profitChange = CalculatePercentageChange(estimatedProfit, previousEstimatedProfit);

            var pendingOrders = _context.Orders.Count(o =>
                (o.OrderStatus == "Pending" || o.OrderStatus == "Processing") &&
                (!applyOwnerFilter || o.OwnerUserID == ownerUserId));

            var openInquiries = _context.TechnicalInquiries.Count(t =>
                !t.IsResolved &&
                (!applyOwnerFilter || t.OwnerUserID == ownerUserId));

            var completedOrders = _context.Orders.Count(o =>
                (o.OrderStatus == "Completed" || o.OrderStatus == "Paid") &&
                (!applyOwnerFilter || o.OwnerUserID == ownerUserId));

            // CALC-KPI: Active customers are distinct buyers in the selected period vs. the previous period.
            var activeCustomers = _context.Orders
                .Where(o =>
                    o.OrderDate >= rangeStartUtc &&
                    o.OrderDate <= rangeEndUtc &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();

            var previousActiveCustomers = _context.Orders
                .Where(o =>
                    o.OrderDate >= previousRangeStart &&
                    o.OrderDate <= previousRangeEnd &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();
            var customerChange = CalculatePercentageChange(activeCustomers, previousActiveCustomers);

            var activeProducts = _context.Products
                .Where(p => p.MarketPriceSource != "Archived")
                .Where(p => !applyOwnerFilter || p.OwnerUserID == ownerUserId)
                .Select(p => new { p.ProductID, p.StockQuantity })
                .ToList();

            var periodMovements = _context.InventoryMovements
                .Where(movement =>
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtc &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();

            // CALC-KPI: Stock health uses at-risk SKU share: (Out of Stock + Low Stock) / Active SKUs.
            var activeSkuCount = activeProducts.Count;
            var atRiskSkuCount = activeProducts.Count(product => product.StockQuantity <= _lowStockThreshold);
            var currentAtRiskPercentage = CalculateRatioPercentage(atRiskSkuCount, activeSkuCount);

            var movementSummaryByProduct = periodMovements
                .GroupBy(movement => movement.ProductID)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        StockIn = group
                            .Where(movement => movement.MovementType == "StockIn")
                            .Sum(movement => Math.Max(0, movement.QuantityDelta)),
                        StockOut = group
                            .Where(movement => movement.MovementType == "StockOut")
                            .Sum(movement => Math.Abs(movement.QuantityDelta))
                    });

            var estimatedStartingAtRiskSkuCount = activeProducts.Count(product =>
            {
                if (!movementSummaryByProduct.TryGetValue(product.ProductID, out var movementSummary))
                {
                    return product.StockQuantity <= _lowStockThreshold;
                }

                var estimatedStartingStock = product.StockQuantity - movementSummary.StockIn + movementSummary.StockOut;
                return estimatedStartingStock <= _lowStockThreshold;
            });

            var estimatedStartingAtRiskPercentage = CalculateRatioPercentage(estimatedStartingAtRiskSkuCount, activeSkuCount);
            var stockHealthPercentage = activeSkuCount > 0
                ? Math.Round(100d - currentAtRiskPercentage, 2)
                : 0d;
            var estimatedStartingHealthPercentage = activeSkuCount > 0
                ? Math.Round(100d - estimatedStartingAtRiskPercentage, 2)
                : 0d;
            var stockHealthChangePercentage = Math.Round(stockHealthPercentage - estimatedStartingHealthPercentage, 2);

            var salesHistory = _inventoryService.GetSalesChartData(rangeStartUtc, rangeEndUtc);
            var categorySales = _inventoryService.GetSalesByCategory(rangeStartUtc, rangeEndUtc, out var categoryLabels);
            // CALC-KPI: Convert category share values into rounded integer percentages for the chart UI.
            var categoryPercentages = categorySales
                .Select(amount => (int)Math.Round(amount, MidpointRounding.AwayFromZero))
                .ToList();

            return new HomeDashboardViewModel
            {
                FilterPeriod = normalizedRange,
                StartDate = rangeStartLocalDate,
                EndDate = rangeEndLocalDate,

                TotalSales = filteredTotalSales,
                SalesChangePercentage = salesChange,

                TotalProfits = estimatedProfit,
                ProfitChangePercentage = profitChange,

                ActiveSkuCount = activeSkuCount,
                AtRiskSkuCount = atRiskSkuCount,
                StockAtRiskPercentage = currentAtRiskPercentage,
                StockHealthPercentage = stockHealthPercentage,
                StockHealthChangePercentage = stockHealthChangePercentage,

                ActiveCustomers = activeCustomers,
                CustomerChangePercentage = customerChange,

                SalesChartData = salesHistory.Select(s => s.Amount).ToList(),
                SalesChartLabels = salesHistory.Select(s => s.Month).ToList(),

                CategoryLabels = categoryLabels,
                CategoryData = categoryPercentages,

                FastSellingProducts = _inventoryService.GetTopSellingProducts(4, rangeStartUtc, rangeEndUtc),
                RecentOrders = _inventoryService.GetRecentOrders(5, rangeStartUtc, rangeEndUtc),

                LowStockCount = inventoryStats.LowStockCount,
                LowStockSkus = inventoryStats.LowStockSkus,

                PendingOrdersCount = pendingOrders,
                OpenInquiriesCount = openInquiries,
                CompletedOrdersCount = completedOrders
            };
        }

        private static string NormalizeDateRange(string? range)
        {
            if (string.IsNullOrWhiteSpace(range))
            {
                return "this_month";
            }

            return range.Trim().ToLowerInvariant() switch
            {
                "today" => "today",
                "yesterday" => "yesterday",
                "last_7_days" => "last_7_days",
                "this_month" => "this_month",
                "this_year" => "this_year",
                "custom" => "custom",
                _ => "this_month"
            };
        }

        private static string MapLegacyFilterToRange(string? legacyFilter)
        {
            if (string.IsNullOrWhiteSpace(legacyFilter))
            {
                return "this_month";
            }

            return legacyFilter.Trim() switch
            {
                "Today" => "today",
                "Yesterday" => "yesterday",
                "Last7Days" => "last_7_days",
                "Monthly" => "this_month",
                "Annual" => "this_year",
                "Year" => "this_year",
                "Custom" => "custom",
                _ => "this_month"
            };
        }

        private static (DateTime StartDate, DateTime EndDate) ResolveDateRange(
            string normalizedRange,
            DateTime? startDate,
            DateTime? endDate)
        {
            var today = BusinessTime.Today;
            var result = normalizedRange switch
            {
                "today" => (today, today),
                "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
                "last_7_days" => (today.AddDays(-6), today),
                "this_month" => (new DateTime(today.Year, today.Month, 1), today),
                "this_year" => (new DateTime(today.Year, 1, 1), today),
                "custom" when startDate.HasValue && endDate.HasValue => (startDate.Value.Date, endDate.Value.Date),
                "custom" when startDate.HasValue => (startDate.Value.Date, startDate.Value.Date),
                "custom" when endDate.HasValue => (endDate.Value.Date, endDate.Value.Date),
                _ => (new DateTime(today.Year, today.Month, 1), today)
            };

            if (result.Item2 < result.Item1)
            {
                return (result.Item1, result.Item1);
            }

            return result;
        }

        private static double CalculatePercentageChange(decimal current, decimal previous)
        {
            // CALC-HELPER: Standardized percent-change math with a divide-by-zero guard for dashboard metrics.
            if (previous == 0m)
            {
                return current == 0m ? 0d : 100d;
            }

            return (double)Math.Round(((current - previous) / previous) * 100m, 2);
        }

        private static double CalculatePercentageChange(int current, int previous)
        {
            return CalculatePercentageChange((decimal)current, (decimal)previous);
        }

        private static double CalculateRatioPercentage(int numerator, int denominator)
        {
            if (denominator <= 0)
            {
                return 0d;
            }

            return Math.Round((numerator / (double)denominator) * 100d, 2);
        }

        private static DateTime ConvertLocalDateStartToUtc(DateTime localDate)
        {
            return BusinessTime.ConvertBusinessDateStartToUtc(localDate);
        }

        private static DateTime ConvertLocalDateEndToUtc(DateTime localDate)
        {
            return BusinessTime.ConvertBusinessDateEndToUtc(localDate);
        }

        // Action of Privacy
        public IActionResult Privacy()
        {
            return View();
        }

        // Action of Error
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

