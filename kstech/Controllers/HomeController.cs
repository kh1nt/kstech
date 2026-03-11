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

            var model = BuildHomeDashboardModel(
                normalizedRange,
                resolvedStartDate.Date,
                resolvedEndDate.Date,
                applyOwnerFilter,
                ownerUserId);

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

            // CALC-KPI: Resolve a comparison window tuned per range preset (month/year are compared as prior MTD/YTD).
            var (comparisonStartLocalDate, comparisonEndLocalDate) = ResolveComparisonDateRange(
                normalizedRange,
                rangeStartLocalDate,
                rangeEndLocalDate);
            var comparisonRangeStartUtc = ConvertLocalDateStartToUtc(comparisonStartLocalDate);
            var comparisonRangeEndUtc = ConvertLocalDateEndToUtc(comparisonEndLocalDate);
            if (comparisonRangeEndUtc < comparisonRangeStartUtc)
            {
                comparisonRangeEndUtc = comparisonRangeStartUtc.AddDays(1).AddTicks(-1);
            }
            var periodComparisonLabel = BuildComparisonLabel(comparisonStartLocalDate, comparisonEndLocalDate);

            var inventoryStats = _inventoryService.GetDashboardStats(rangeStartLocalDate, rangeEndLocalDate);
            // CALC-KPI: Sales KPI and period-over-period sales change for the dashboard summary cards.
            var filteredTotalSales = _inventoryService.GetTotalSales(rangeStartUtc, rangeEndUtc);
            var previousTotalSales = _inventoryService.GetTotalSales(comparisonRangeStartUtc, comparisonRangeEndUtc);
            var salesChange = CalculatePercentageChange(filteredTotalSales, previousTotalSales);

            // CALC-KPI: Estimate COGS from order details to derive profit without a separate financial snapshot table.
            var currentCogs = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od =>
                    od.OrderID != 0 &&
                    od.Order != null &&
                    od.Order.OrderStatus != RevenueRecognitionPolicy.CancelledOrderStatus &&
                    od.Order.PaymentStatus == RevenueRecognitionPolicy.PaidPaymentStatus &&
                    od.Order.PaymentStatus != RevenueRecognitionPolicy.RefundedPaymentStatus &&
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
                    od.Order.OrderStatus != RevenueRecognitionPolicy.CancelledOrderStatus &&
                    od.Order.PaymentStatus == RevenueRecognitionPolicy.PaidPaymentStatus &&
                    od.Order.PaymentStatus != RevenueRecognitionPolicy.RefundedPaymentStatus &&
                    od.Order.OrderDate >= comparisonRangeStartUtc &&
                    od.Order.OrderDate <= comparisonRangeEndUtc &&
                    (!applyOwnerFilter || od.Order.OwnerUserID == ownerUserId) &&
                    od.Product != null)
                .Sum(od => od.Quantity * od.Product!.CostPrice);

            // CALC-KPI: Profit KPI is estimated as sales minus COGS for current and previous periods.
            var estimatedProfit = filteredTotalSales - currentCogs;
            var previousEstimatedProfit = previousTotalSales - previousCogs;
            var profitChange = CalculatePercentageChangeUsingAbsoluteBaseline(estimatedProfit, previousEstimatedProfit);

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
            var activeCustomers = ApplyRecognizedRevenueOrderFilter(_context.Orders)
                .Where(o =>
                    o.OrderDate >= rangeStartUtc &&
                    o.OrderDate <= rangeEndUtc &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();

            var previousActiveCustomers = ApplyRecognizedRevenueOrderFilter(_context.Orders)
                .Where(o =>
                    o.OrderDate >= comparisonRangeStartUtc &&
                    o.OrderDate <= comparisonRangeEndUtc &&
                    (!applyOwnerFilter || o.OwnerUserID == ownerUserId))
                .Select(o => o.CustomerID)
                .Distinct()
                .Count();
            var customerChangeCount = activeCustomers - previousActiveCustomers;

            var activeProducts = _context.Products
                .Where(p => p.MarketPriceSource != "Archived")
                .Where(p => !applyOwnerFilter || p.OwnerUserID == ownerUserId)
                .Select(p => new { p.ProductID, p.StockQuantity })
                .ToList();

            // CALC-KPI: Reconstruct stock as-of the selected end date by reversing movement activity that happened after the range.
            var postRangeMovements = _context.InventoryMovements
                .Where(movement =>
                    movement.OccurredAtUtc > rangeEndUtc &&
                    (movement.MovementType == "StockIn" || movement.MovementType == "StockOut") &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();
            var postRangeMovementSummaryByProduct = BuildStockMovementSummaryByProduct(postRangeMovements);
            var stockSnapshotAtRangeEnd = activeProducts
                .Select(product =>
                {
                    if (!postRangeMovementSummaryByProduct.TryGetValue(product.ProductID, out var postRangeSummary))
                    {
                        return new
                        {
                            product.ProductID,
                            StockQuantityAtRangeEnd = Math.Max(0, product.StockQuantity)
                        };
                    }

                    var stockQuantityAtRangeEnd = Math.Max(
                        0,
                        product.StockQuantity - postRangeSummary.StockInUnits + postRangeSummary.StockOutUnits);
                    return new
                    {
                        product.ProductID,
                        StockQuantityAtRangeEnd = stockQuantityAtRangeEnd
                    };
                })
                .ToList();

            var periodMovements = _context.InventoryMovements
                .Where(movement =>
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtc &&
                    (movement.MovementType == "StockIn" || movement.MovementType == "StockOut") &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();

            // CALC-KPI: Stock health uses at-risk SKU share: (Out of Stock + Low Stock) / Active SKUs.
            var activeSkuCount = stockSnapshotAtRangeEnd.Count;
            var atRiskSkuCount = stockSnapshotAtRangeEnd.Count(product => product.StockQuantityAtRangeEnd <= _lowStockThreshold);
            var currentAtRiskPercentage = CalculateRatioPercentage(atRiskSkuCount, activeSkuCount);

            var periodMovementSummaryByProduct = BuildStockMovementSummaryByProduct(periodMovements);

            var estimatedStartingAtRiskSkuCount = stockSnapshotAtRangeEnd.Count(product =>
            {
                if (!periodMovementSummaryByProduct.TryGetValue(product.ProductID, out var movementSummary))
                {
                    return product.StockQuantityAtRangeEnd <= _lowStockThreshold;
                }

                var estimatedStartingStock = product.StockQuantityAtRangeEnd - movementSummary.StockInUnits + movementSummary.StockOutUnits;
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
                PeriodComparisonLabel = periodComparisonLabel,

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
                CustomerChangeCount = customerChangeCount,

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

        private static (DateTime StartDate, DateTime EndDate) ResolveComparisonDateRange(
            string normalizedRange,
            DateTime currentStartDate,
            DateTime currentEndDate)
        {
            var normalizedStart = currentStartDate.Date;
            var normalizedEnd = currentEndDate.Date < normalizedStart ? normalizedStart : currentEndDate.Date;
            var spanDays = Math.Max(1, (normalizedEnd - normalizedStart).Days + 1);

            return normalizedRange switch
            {
                "today" => (normalizedStart.AddDays(-1), normalizedStart.AddDays(-1)),
                "yesterday" => (normalizedStart.AddDays(-1), normalizedStart.AddDays(-1)),
                "this_month" => ResolvePreviousMonthToDate(normalizedEnd, spanDays),
                "this_year" => ResolvePreviousYearToDate(normalizedEnd, spanDays),
                "custom" => ResolvePreviousMonthSameDates(normalizedStart, normalizedEnd),
                _ => (normalizedStart.AddDays(-spanDays), normalizedStart.AddDays(-1))
            };
        }

        private static (DateTime StartDate, DateTime EndDate) ResolvePreviousMonthToDate(DateTime currentEndDate, int spanDays)
        {
            var referenceMonth = currentEndDate.Date.AddMonths(-1);
            var previousMonthStart = new DateTime(referenceMonth.Year, referenceMonth.Month, 1);
            var previousMonthDayCount = DateTime.DaysInMonth(referenceMonth.Year, referenceMonth.Month);
            var cappedSpanDays = Math.Clamp(spanDays, 1, previousMonthDayCount);
            return (previousMonthStart, previousMonthStart.AddDays(cappedSpanDays - 1));
        }

        private static (DateTime StartDate, DateTime EndDate) ResolvePreviousYearToDate(DateTime currentEndDate, int spanDays)
        {
            var previousYearStart = new DateTime(currentEndDate.Year - 1, 1, 1);
            var previousYearDayCount = DateTime.IsLeapYear(previousYearStart.Year) ? 366 : 365;
            var cappedSpanDays = Math.Clamp(spanDays, 1, previousYearDayCount);
            return (previousYearStart, previousYearStart.AddDays(cappedSpanDays - 1));
        }

        private static (DateTime StartDate, DateTime EndDate) ResolvePreviousMonthSameDates(
            DateTime currentStartDate,
            DateTime currentEndDate)
        {
            var previousStart = currentStartDate.AddMonths(-1);
            var previousEnd = currentEndDate.AddMonths(-1);
            if (previousEnd < previousStart)
            {
                previousEnd = previousStart;
            }

            return (previousStart, previousEnd);
        }

        private static string BuildComparisonLabel(DateTime comparisonStartDate, DateTime comparisonEndDate)
        {
            var normalizedStart = comparisonStartDate.Date;
            var normalizedEnd = comparisonEndDate.Date < normalizedStart ? normalizedStart : comparisonEndDate.Date;

            if (normalizedStart == normalizedEnd)
            {
                return $"Compared with {normalizedStart:MMM dd, yyyy}";
            }

            return $"Compared with {normalizedStart:MMM dd, yyyy} - {normalizedEnd:MMM dd, yyyy}";
        }

        private static IQueryable<Order> ApplyRecognizedRevenueOrderFilter(IQueryable<Order> query)
        {
            return RevenueRecognitionPolicy.ApplyRecognizedRevenueOrderFilter(query);
        }

        private static Dictionary<int, StockMovementSummary> BuildStockMovementSummaryByProduct(IEnumerable<InventoryMovement> movements)
        {
            return movements
                .GroupBy(movement => movement.ProductID)
                .ToDictionary(
                    group => group.Key,
                    group => new StockMovementSummary
                    {
                        StockInUnits = group
                            .Where(movement => movement.MovementType == "StockIn")
                            .Sum(movement => Math.Max(0, movement.QuantityDelta)),
                        StockOutUnits = group
                            .Where(movement => movement.MovementType == "StockOut")
                            .Sum(movement => Math.Abs(movement.QuantityDelta))
                    });
        }

        private static double CalculatePercentageChange(decimal current, decimal previous)
        {
            // CALC-HELPER: Standardized percent-change math with a divide-by-zero guard for dashboard metrics.
            if (previous == 0m)
            {
                if (current == 0m)
                {
                    return 0d;
                }

                return current > 0m ? 100d : -100d;
            }

            return (double)Math.Round(((current - previous) / previous) * 100m, 2);
        }

        private static double CalculatePercentageChangeUsingAbsoluteBaseline(decimal current, decimal previous)
        {
            // CALC-HELPER: Profit trend normalizes negative baselines so improving from loss->profit reads as positive.
            if (previous == 0m)
            {
                if (current == 0m)
                {
                    return 0d;
                }

                return current > 0m ? 100d : -100d;
            }

            return (double)Math.Round(((current - previous) / Math.Abs(previous)) * 100m, 2);
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

        private sealed class StockMovementSummary
        {
            public int StockInUnits { get; init; }
            public int StockOutUnits { get; init; }
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

