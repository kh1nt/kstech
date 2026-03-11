using kstech.Data;
using kstech.Models.Entities;
using kstech.Models.ViewModels;
using kstech.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace kstech.Services
{
    public interface IBusinessIntelligenceService
    {
        SalesAnalyticsViewModel GetSalesAnalytics(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string selectedDateRange = "this_month",
            string paymentFilter = "all",
            string orderStatusFilter = "all",
            int recentSalesPage = 1,
            int recentSalesPageSize = 10);

        FinancialPerformanceViewModel GetFinancialPerformance(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string selectedDateRange = "this_month",
            string paymentScope = "paid_only",
            int recentTransactionsPage = 1,
            int recentTransactionsPageSize = 10);

        BusinessIntelligenceViewModel GetBusinessIntelligence(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string adjustmentFilter = "all");

        BudgetPlanningViewModel GetBudgetPlanning(
            int? selectedBudgetId = null,
            bool showArchivedBudgets = false,
            DateTime? selectedMonthLocal = null);

        bool TryUpdateProductPrice(int productId, decimal newPrice);
        BudgetSaveResult SaveFinancialBudget(
            int? selectedBudgetId,
            DateTime budgetMonthLocal,
            decimal budgetAmount,
            string? changeReason);
        bool TryArchiveFinancialBudget(int budgetId, out string message);
        bool TryRestoreFinancialBudget(int budgetId, out string message);
    }

    public sealed class BudgetSaveResult
    {
        public bool Succeeded { get; set; }
        public int? BudgetId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BusinessIntelligenceService : IBusinessIntelligenceService
    {
        private const string ArchivedMarketPriceSource = "Archived";
        private static readonly int[] AllowedPageSizes = { 10, 20, 50 };
        private static readonly HashSet<string> PendingStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pending",
            "Processing"
        };

        private static readonly HashSet<string> CompletedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Completed",
            "Paid"
        };

        private const string BudgetStatusActive = "Active";
        private const string BudgetStatusArchived = "Archived";
        private const string BudgetStatusDeleted = "Deleted";
        private const string BudgetEventTypeCreate = "Create";
        private const string BudgetEventTypeUpdate = "Update";
        private const string BudgetEventTypeArchive = "Archive";
        private const string BudgetEventTypeRestore = "Restore";
        private const string BudgetEventTypeReserve = "Reserve";
        private const string BudgetEventTypeSpend = "Spend";
        private const string BudgetEventTypeRelease = "Release";
        private const string BudgetEventReferenceTypeBudget = "Budget";
        private const string BudgetEventReferenceTypePurchaseOrder = "PurchaseOrder";
        private const string BudgetHistoryLogPrefix = "Budget";
        private const string BudgetHistoryBudgetTagPrefix = "[BudgetId:";
        private const string PurchaseOrderStatusApproved = "Approved";
        private const string PurchaseOrderStatusPartiallyReceived = "PartiallyReceived";
        private const string PurchaseOrderStatusReceived = "Received";
        private const string ProcurementPlanMovementType = "ProcurementPlan";
        private const string ProcurementPlanReasonPrefix = "PROC-PLAN";
        private const string ProcurementReferenceType = "PurchaseOrder";

        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly ILoyaltyService _loyaltyService;
        private bool? _hasPurchaseOrderTables;

        public BusinessIntelligenceService(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ILoyaltyService loyaltyService)
        {
            _context = context;
            _tenantContext = tenantContext;
            _loyaltyService = loyaltyService;
        }

        public SalesAnalyticsViewModel GetSalesAnalytics(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string selectedDateRange = "this_month",
            string paymentFilter = "all",
            string orderStatusFilter = "all",
            int recentSalesPage = 1,
            int recentSalesPageSize = 10)
        {
            var normalizedPaymentFilter = NormalizePaymentFilter(paymentFilter);
            var normalizedOrderStatusFilter = NormalizeOrderStatusFilter(orderStatusFilter);
            var normalizedRecentSalesPageSize = NormalizePageSize(recentSalesPageSize);
            var normalizedSelectedDateRange = NormalizeSelectedDateRange(selectedDateRange);
            var (rangeStartLocalDate, rangeEndLocalDate) = NormalizeLocalDateRange(startDateUtc, endDateUtc);
            var (rangeStartUtc, rangeEndUtcInclusive) = NormalizeRange(startDateUtc, endDateUtc);
            var (comparisonStartLocalDate, comparisonEndLocalDate) = ResolveComparisonLocalDateRange(
                normalizedSelectedDateRange,
                rangeStartLocalDate,
                rangeEndLocalDate);
            var comparisonPeriodLabel = BuildComparisonPeriodLabel(comparisonStartLocalDate, comparisonEndLocalDate);
            var comparisonStartUtc = ConvertLocalDateStartToUtc(comparisonStartLocalDate);
            var comparisonEndUtcInclusive = ConvertLocalDateEndToUtc(comparisonEndLocalDate);
            var (applyOwnerFilter, ownerUserId) = ResolveOwnerFilterContext();

            var thisPeriodOrders = ApplyOwnerFilter(_context.Orders.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Include(order => order.Customer)
                .Include(order => order.OrderDetails)
                .ThenInclude(detail => detail.Product)
                .Where(order => order.OrderDate >= rangeStartUtc && order.OrderDate <= rangeEndUtcInclusive)
                .ToList();

            var previousPeriodOrders = ApplyOwnerFilter(_context.Orders.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(order => order.OrderDate >= comparisonStartUtc && order.OrderDate <= comparisonEndUtcInclusive)
                .ToList();

            // CALC-KPI: Build normalized current/previous filtered datasets so all metrics use the same scope.
            var filteredOrders = thisPeriodOrders
                .Where(order =>
                    MatchesPaymentFilter(order, normalizedPaymentFilter) &&
                    MatchesOrderStatusFilter(order, normalizedOrderStatusFilter))
                .ToList();

            var previousFilteredOrders = previousPeriodOrders
                .Where(order =>
                    MatchesPaymentFilter(order, normalizedPaymentFilter) &&
                    MatchesOrderStatusFilter(order, normalizedOrderStatusFilter))
                .ToList();
            var recognizedRevenueOrders = filteredOrders
                .Where(RevenueRecognitionPolicy.IsRecognizedRevenueOrder)
                .ToList();
            var previousRecognizedRevenueOrders = previousFilteredOrders
                .Where(RevenueRecognitionPolicy.IsRecognizedRevenueOrder)
                .ToList();

            // CALC-KPI: Core sales KPIs (revenue, orders, order states) are derived from the filtered order set.
            var totalRevenue = recognizedRevenueOrders.Sum(order => order.TotalAmount);
            var previousRevenue = previousRecognizedRevenueOrders.Sum(order => order.TotalAmount);

            var totalOrders = filteredOrders.Count;
            var previousTotalOrders = previousFilteredOrders.Count;

            var paidOrders = filteredOrders.Count(order => IsPaidStatus(order.PaymentStatus));
            var refundedOrders = filteredOrders.Count(order => IsRefundedStatus(order.PaymentStatus));
            var pendingOrders = filteredOrders.Count(order => IsPendingStatus(order.OrderStatus));
            var completedOrders = filteredOrders.Count(order => IsCompletedStatus(order.OrderStatus));

            var (trendLabels, trendValues) = BuildRevenueTrend(
                filteredOrders,
                rangeStartLocalDate,
                rangeEndLocalDate);

            // CALC-KPI: Payment breakdown percentages are normalized against total filtered orders.
            var paymentBreakdown = filteredOrders
                .GroupBy(order => string.IsNullOrWhiteSpace(order.PaymentStatus) ? "Unknown" : order.PaymentStatus.Trim())
                .Select(group =>
                {
                    var count = group.Count();
                    var percentage = totalOrders > 0
                        ? Math.Round((count / (decimal)totalOrders) * 100m, 2)
                        : 0m;

                    return new SalesBreakdownItemViewModel
                    {
                        Label = group.Key,
                        Count = count,
                        Percentage = percentage
                    };
                })
                .OrderByDescending(item => item.Count)
                .ToList();

            var fastMovingItems = recognizedRevenueOrders
                .SelectMany(order => order.OrderDetails
                    .Where(detail => detail.Product != null)
                    .Select(detail => new
                    {
                        detail.ProductID,
                        ProductName = detail.Product!.ProductName,
                        CategoryName = detail.Product.CategoryName,
                        detail.Quantity,
                        detail.SubTotal
                    }))
                .GroupBy(item => new
                {
                    item.ProductID,
                    item.ProductName,
                    item.CategoryName
                })
                .Select(group => new FastMovingItemViewModel
                {
                    ProductId = group.Key.ProductID,
                    ProductName = group.Key.ProductName,
                    CategoryName = string.IsNullOrWhiteSpace(group.Key.CategoryName)
                        ? "Uncategorized"
                        : group.Key.CategoryName!.Trim(),
                    UnitsSold = group.Sum(item => item.Quantity),
                    Revenue = Math.Round(group.Sum(item => item.SubTotal), 2, MidpointRounding.AwayFromZero)
                })
                .OrderByDescending(item => item.UnitsSold)
                .ThenByDescending(item => item.Revenue)
                .Take(6)
                .ToList();

            var loyaltyRules = _loyaltyService.GetProgramRules();
            var recentOrderSnapshots = filteredOrders
                .OrderByDescending(order => order.OrderDate)
                .Select(order => new SalesOrderSnapshotViewModel
                {
                    LineItems = order.OrderDetails
                        .Select(detail => new SalesOrderLineItemViewModel
                        {
                            ProductName = string.IsNullOrWhiteSpace(detail.Product?.ProductName)
                                ? $"Product #{detail.ProductID}"
                                : detail.Product!.ProductName,
                            Quantity = detail.Quantity,
                            UnitPrice = detail.UnitPriceAtSale,
                            Subtotal = detail.SubTotal
                        })
                        .ToList(),
                    OrderId = order.OrderID,
                    CustomerName = ResolveCustomerName(order.Customer),
                    OrderDate = order.OrderDate,
                    TotalAmount = order.TotalAmount,
                    SubtotalBeforeDiscount = Math.Round(order.TotalAmount + order.LoyaltyDiscountAmount, 2, MidpointRounding.AwayFromZero),
                    LoyaltyDiscountAmount = Math.Round(order.LoyaltyDiscountAmount, 2, MidpointRounding.AwayFromZero),
                    LoyaltyPointsRedeemed = order.LoyaltyPointsRedeemed,
                    LoyaltyPointsEarned = order.LoyaltyPointsEarned,
                    LoyaltyProgramEnabled = loyaltyRules.Enabled,
                    LoyaltyPointValue = loyaltyRules.PointRedemptionValue,
                    LoyaltyBasePointsPerCurrency = loyaltyRules.BasePointsPerCurrency,
                    LoyaltyBasePointsRaw = Math.Round(order.TotalAmount * loyaltyRules.BasePointsPerCurrency, 2, MidpointRounding.AwayFromZero),
                    TotalItems = order.OrderDetails.Sum(detail => detail.Quantity),
                    PaymentStatus = string.IsNullOrWhiteSpace(order.PaymentStatus) ? "Unknown" : order.PaymentStatus,
                    OrderStatus = string.IsNullOrWhiteSpace(order.OrderStatus) ? "Unknown" : order.OrderStatus,
                    ProductsSummary = string.Join(", ", order.OrderDetails
                        .Select(detail => detail.Product?.ProductName)
                        .Where(productName => !string.IsNullOrWhiteSpace(productName))
                        .Distinct()
                        .Take(2)) switch
                    {
                        "" => "No line items",
                        var summary when order.OrderDetails
                            .Select(detail => detail.Product?.ProductName)
                            .Where(productName => !string.IsNullOrWhiteSpace(productName))
                            .Distinct()
                            .Count() > 2 => $"{summary} +{order.OrderDetails
                                .Select(detail => detail.Product?.ProductName)
                                .Where(productName => !string.IsNullOrWhiteSpace(productName))
                                .Distinct()
                                .Count() - 2} more",
                        var summary => summary
                    }
                })
                .ToList();

            var recentSalesTotalCount = recentOrderSnapshots.Count;
            var recentSalesTotalPages = CalculateTotalPages(recentSalesTotalCount, normalizedRecentSalesPageSize);
            var normalizedRecentSalesPage = NormalizePage(recentSalesPage, recentSalesTotalPages);
            var recentOrders = recentOrderSnapshots
                .Skip((normalizedRecentSalesPage - 1) * normalizedRecentSalesPageSize)
                .Take(normalizedRecentSalesPageSize)
                .ToList();

            // CALC-KPI: Assemble final sales analytics KPIs and period-over-period deltas for the UI.
            return new SalesAnalyticsViewModel
            {
                SelectedDateRange = normalizedSelectedDateRange,
                SelectedPaymentFilter = normalizedPaymentFilter,
                SelectedOrderStatusFilter = normalizedOrderStatusFilter,
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                ComparisonPeriodLabel = comparisonPeriodLabel,
                TotalRevenue = totalRevenue,
                TotalOrders = totalOrders,
                AverageOrderValue = totalOrders > 0 ? Math.Round(totalRevenue / totalOrders, 2) : 0m,
                ActiveOrders = filteredOrders.Count(order => !IsCompletedStatus(order.OrderStatus)),
                PendingOrders = pendingOrders,
                CompletedOrders = completedOrders,
                PaidOrders = paidOrders,
                RefundedOrders = refundedOrders,
                RevenueChangePercentage = CalculateChangePercentage(totalRevenue, previousRevenue),
                OrderChangePercentage = CalculateChangePercentage(totalOrders, previousTotalOrders),
                PaidOrderRate = totalOrders > 0
                    ? Math.Round((paidOrders / (decimal)totalOrders) * 100m, 2)
                    : 0m,
                SalesTrendLabels = trendLabels,
                SalesTrendValues = trendValues,
                PaymentStatusBreakdown = paymentBreakdown,
                FastMovingItems = fastMovingItems,
                RecentOrders = recentOrders,
                RecentSalesPage = normalizedRecentSalesPage,
                RecentSalesPageSize = normalizedRecentSalesPageSize,
                RecentSalesTotalCount = recentSalesTotalCount
            };
        }

        public FinancialPerformanceViewModel GetFinancialPerformance(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string selectedDateRange = "this_month",
            string paymentScope = "paid_only",
            int recentTransactionsPage = 1,
            int recentTransactionsPageSize = 10)
        {
            var normalizedPaymentScope = NormalizeFinancialPaymentScope(paymentScope);
            var normalizedRecentTransactionsPageSize = NormalizePageSize(recentTransactionsPageSize);
            var normalizedSelectedDateRange = NormalizeSelectedDateRange(selectedDateRange);
            var (rangeStartLocalDate, rangeEndLocalDate) = NormalizeLocalDateRange(startDateUtc, endDateUtc);
            var (rangeStartUtc, rangeEndUtcInclusive) = NormalizeRange(startDateUtc, endDateUtc);
            var (comparisonStartLocalDate, comparisonEndLocalDate) = ResolveComparisonLocalDateRange(
                normalizedSelectedDateRange,
                rangeStartLocalDate,
                rangeEndLocalDate);
            var comparisonPeriodLabel = BuildComparisonPeriodLabel(comparisonStartLocalDate, comparisonEndLocalDate);
            var comparisonStartUtc = ConvertLocalDateStartToUtc(comparisonStartLocalDate);
            var comparisonEndUtcInclusive = ConvertLocalDateEndToUtc(comparisonEndLocalDate);
            var (applyOwnerFilter, ownerUserId) = ResolveOwnerFilterContext();

            var thisPeriodOrders = ApplyOwnerFilter(_context.Orders.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Include(order => order.OrderDetails)
                .ThenInclude(detail => detail.Product)
                .Where(order => order.OrderDate >= rangeStartUtc && order.OrderDate <= rangeEndUtcInclusive)
                .ToList();

            var previousPeriodOrders = ApplyOwnerFilter(_context.Orders.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Include(order => order.OrderDetails)
                .ThenInclude(detail => detail.Product)
                .Where(order => order.OrderDate >= comparisonStartUtc && order.OrderDate <= comparisonEndUtcInclusive)
                .ToList();

            var filteredOrders = thisPeriodOrders
                .Where(order => MatchesFinancialPaymentScope(order, normalizedPaymentScope))
                .ToList();

            var previousFilteredOrders = previousPeriodOrders
                .Where(order => MatchesFinancialPaymentScope(order, normalizedPaymentScope))
                .ToList();

            var thisPeriodUnitCostLookup = BuildOrderProductUnitCostLookup(
                filteredOrders.Select(order => order.OrderID),
                applyOwnerFilter,
                ownerUserId);
            var previousPeriodUnitCostLookup = BuildOrderProductUnitCostLookup(
                previousFilteredOrders.Select(order => order.OrderID),
                applyOwnerFilter,
                ownerUserId);

            var thisPeriodSummary = BuildFinancialSummary(filteredOrders, thisPeriodUnitCostLookup);
            var previousPeriodSummary = BuildFinancialSummary(previousFilteredOrders, previousPeriodUnitCostLookup);

            var (dailyProfitLabels, dailyCogsValues, dailyRevenueValues) = BuildProfitTrend(
                filteredOrders,
                rangeStartLocalDate,
                rangeEndLocalDate,
                thisPeriodUnitCostLookup);

            var categoryProfit = filteredOrders
                .SelectMany(order => order.OrderDetails)
                .Where(detail => detail.Product != null)
                .GroupBy(detail => detail.Product!.CategoryName ?? "Uncategorized")
                .Select(group =>
                {
                    var revenue = group.Sum(detail => detail.SubTotal);
                    var cogs = group.Sum(detail =>
                        detail.Quantity * ResolveUnitCost(detail, thisPeriodUnitCostLookup));
                    return new
                    {
                        Category = group.Key,
                        Profit = revenue - cogs
                    };
                })
                .OrderByDescending(item => item.Profit)
                .ToList();

            var totalCategoryProfit = categoryProfit.Sum(item => item.Profit);
            var categoryProfitItems = categoryProfit
                .Take(6)
                .Select(item => new CategoryProfitItemViewModel
                {
                    Category = item.Category,
                    Profit = item.Profit,
                    Percentage = totalCategoryProfit > 0
                        ? Math.Round((item.Profit / totalCategoryProfit) * 100m, 2)
                        : 0m
                })
                .ToList();

            var recentTransactionSnapshots = filteredOrders
                .SelectMany(order => order.OrderDetails.Select(detail => new { order, detail }))
                .Where(item => item.detail.Product != null)
                .OrderByDescending(item => item.order.OrderDate)
                .Select(item =>
                {
                    var cost = item.detail.Quantity * ResolveUnitCost(item.detail, thisPeriodUnitCostLookup);
                    var profit = item.detail.SubTotal - cost;

                    return new FinancialTransactionViewModel
                    {
                        TransactionId = $"TRX-{item.order.OrderID}-{item.detail.DetailID}",
                        ProductName = item.detail.Product!.ProductName,
                        Category = item.detail.Product.CategoryName ?? "Uncategorized",
                        SalePrice = item.detail.SubTotal,
                        Cost = cost,
                        Profit = profit,
                        Status = string.IsNullOrWhiteSpace(item.order.PaymentStatus) ? "Unknown" : item.order.PaymentStatus,
                        TransactionDate = item.order.OrderDate
                    };
                })
                .ToList();

            var recentTransactionsTotalCount = recentTransactionSnapshots.Count;
            var recentTransactionsTotalPages = CalculateTotalPages(recentTransactionsTotalCount, normalizedRecentTransactionsPageSize);
            var normalizedRecentTransactionsPage = NormalizePage(recentTransactionsPage, recentTransactionsTotalPages);
            var recentTransactions = recentTransactionSnapshots
                .Skip((normalizedRecentTransactionsPage - 1) * normalizedRecentTransactionsPageSize)
                .Take(normalizedRecentTransactionsPageSize)
                .ToList();

            var budgetAmount = ResolveBudgetAmount(rangeStartLocalDate, rangeEndLocalDate);

            var budgetVariance = thisPeriodSummary.Revenue - budgetAmount;
            var budgetUtilization = budgetAmount > 0m
                ? Math.Round((thisPeriodSummary.Revenue / budgetAmount) * 100m, 2)
                : 0m;

            var daysInRange = Math.Max(1, (rangeEndLocalDate - rangeStartLocalDate).Days + 1);

            // Fetch Budget/Inventory Planning Data
            var inventoryPlanningItems = BuildInventoryPlanningInsights(
                rangeStartUtc,
                rangeEndUtcInclusive,
                daysInRange,
                applyOwnerFilter,
                ownerUserId);

            var atRiskParts = BuildAtRiskInventoryItems(inventoryPlanningItems);
            var suggestedRestockSpend = atRiskParts.Sum(item => item.RecommendedRestockSpend);





            return new FinancialPerformanceViewModel
            {
                SelectedDateRange = normalizedSelectedDateRange,
                SelectedPaymentScope = normalizedPaymentScope,
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                ComparisonPeriodLabel = comparisonPeriodLabel,
                Revenue = thisPeriodSummary.Revenue,
                CostOfGoodsSold = thisPeriodSummary.CostOfGoodsSold,
                GrossProfit = thisPeriodSummary.GrossProfit,
                NetProfitMargin = thisPeriodSummary.Margin,
                RevenueChangePercentage = CalculateChangePercentage(thisPeriodSummary.Revenue, previousPeriodSummary.Revenue),
                GrossProfitChangePercentage = CalculateChangePercentageUsingAbsoluteBaseline(thisPeriodSummary.GrossProfit, previousPeriodSummary.GrossProfit),
                MarginChangePercentage = CalculateChangePercentageUsingAbsoluteBaseline(thisPeriodSummary.Margin, previousPeriodSummary.Margin),
                DailyProfitLabels = dailyProfitLabels,
                DailyCogsValues = dailyCogsValues,
                DailyRevenueValues = dailyRevenueValues,
                ProfitByCategory = categoryProfitItems,
                RecentTransactions = recentTransactions,
                RecentTransactionsPage = normalizedRecentTransactionsPage,
                RecentTransactionsPageSize = normalizedRecentTransactionsPageSize,
                RecentTransactionsTotalCount = recentTransactionsTotalCount,
                BudgetAmount = budgetAmount,
                BudgetVariance = budgetVariance,
                BudgetUtilizationPercentage = budgetUtilization,
                SuggestedRestockSpend = suggestedRestockSpend,
                AtRiskInventoryItemsCount = atRiskParts.Count,

            };
        }

        public BudgetPlanningViewModel GetBudgetPlanning(
            int? selectedBudgetId = null,
            bool showArchivedBudgets = false,
            DateTime? selectedMonthLocal = null)
        {
            var budgetOwnerUserId = ResolveWritableOwnerUserId();
            var allBudgets = LoadBudgets(budgetOwnerUserId);
            var budgetHistoryLogs = LoadBudgetHistoryLogs(budgetOwnerUserId);
            var budgetEvents = LoadBudgetEvents(budgetOwnerUserId);
            var budgetHistoryLookupFromLogs = BuildBudgetHistoryLookupByBudgetId(budgetHistoryLogs);
            var budgetHistoryLookupFromEvents = BuildBudgetHistoryLookupByBudgetIdFromEvents(budgetEvents);
            var budgetHistoryLookupByBudgetId = MergeBudgetHistoryLookups(
                budgetHistoryLookupFromEvents,
                budgetHistoryLookupFromLogs);
            var budgetUsageLookupByBudgetId = BuildBudgetUsageLookupByBudgetId(budgetEvents);
            var budgetSelectionContext = ResolveBudgetSelection(
                selectedBudgetId,
                selectedMonthLocal,
                allBudgets,
                showArchivedBudgets);

            var selectedBudget = budgetSelectionContext.SelectedBudget;
            var rangeStartLocalDate = budgetSelectionContext.RangeStartDateLocal;
            var rangeEndLocalDate = budgetSelectionContext.RangeEndDateLocal;
            var budgetAmount = budgetSelectionContext.BudgetAmount;
            var rangeStartUtc = ConvertLocalDateStartToUtc(rangeStartLocalDate);
            var rangeEndUtcInclusive = ConvertLocalDateEndToUtc(rangeEndLocalDate);
            var (applyOwnerFilter, ownerUserId) = ResolveOwnerFilterContext();
            var daysInRange = Math.Max(1, (rangeEndLocalDate - rangeStartLocalDate).Days + 1);

            var budgetRevenueAnalytics = BuildBudgetRevenueAnalytics(
                rangeStartUtc,
                rangeEndUtcInclusive,
                applyOwnerFilter,
                ownerUserId);

            var inventoryPlanningItems = BuildInventoryPlanningInsights(
                rangeStartUtc,
                rangeEndUtcInclusive,
                daysInRange,
                applyOwnerFilter,
                ownerUserId);

            var atRiskParts = BuildAtRiskInventoryItems(inventoryPlanningItems);

            var suggestedRestockSpend = atRiskParts.Sum(item => item.RecommendedRestockSpend);
            var budgetUsageSummary = BuildTotalBudgetUsage(budgetEvents, selectedBudget?.BudgetID);
            var actualProcurementSpend = budgetUsageSummary.ReservedAmount + budgetUsageSummary.SpentAmount;

            var budgetPlanningKpis = CalculateBudgetPlanningKpis(
                budgetAmount,
                actualProcurementSpend,
                suggestedRestockSpend,
                budgetUsageSummary.ReservedAmount,
                budgetUsageSummary.SpentAmount);

            var (trendLabels, actualRevenueTrendValues, budgetTargetTrendValues) = BuildBudgetTrackingTrend(
                budgetRevenueAnalytics.FilteredOrders,
                rangeStartLocalDate,
                rangeEndLocalDate,
                budgetAmount);

            var currentBudgetPoLines = new List<PurchaseOrderLine>();
            if (selectedBudgetId.HasValue && applyOwnerFilter)
            {
                currentBudgetPoLines = _context.PurchaseOrders
                    .Include(po => po.Lines)
                    .ThenInclude(line => line.Product)
                    .Where(po => po.BudgetID == selectedBudgetId.Value && po.OwnerUserID == ownerUserId && po.Status != "Cancelled")
                    .SelectMany(po => po.Lines)
                    .ToList();
            }
            else if (selectedBudgetId.HasValue)
            {
                currentBudgetPoLines = _context.PurchaseOrders
                    .Include(po => po.Lines)
                    .ThenInclude(line => line.Product)
                    .Where(po => po.BudgetID == selectedBudgetId.Value && po.Status != "Cancelled")
                    .SelectMany(po => po.Lines)
                    .ToList();
            }

            var suggestedAllocations = BuildBudgetAllocationSuggestions(inventoryPlanningItems, budgetAmount, currentBudgetPoLines);
            var monthlyBudgets = BuildMonthlyBudgetRows(
                allBudgets,
                selectedBudget?.BudgetID,
                showArchivedBudgets,
                budgetHistoryLookupByBudgetId,
                budgetUsageLookupByBudgetId);
            var budgetHistory = selectedBudget != null
                ? BuildBudgetHistoryForBudget(selectedBudget.BudgetID, budgetHistoryLookupByBudgetId)
                : new List<BudgetHistoryItemViewModel>();

            return new BudgetPlanningViewModel
            {
                SelectedBudgetId = selectedBudget?.BudgetID,
                SelectedBudgetStatus = selectedBudget?.Status ?? "Draft",
                ShowArchivedBudgets = showArchivedBudgets,
                BudgetSelectionMessage = budgetSelectionContext.SelectionMessage,
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                SelectedMonthStartDateLocal = rangeStartLocalDate,
                SelectedMonthEndDateLocal = rangeEndLocalDate,
                SelectedHistoryMonthLabel = rangeStartLocalDate.ToString("MMMM yyyy"),
                BudgetAmount = budgetAmount,
                ActualRevenue = budgetRevenueAnalytics.Summary.Revenue,
                GrossProfit = budgetRevenueAnalytics.Summary.GrossProfit,
                BudgetVariance = budgetPlanningKpis.BudgetVariance,
                BudgetUtilizationPercentage = budgetPlanningKpis.BudgetUtilizationPercentage,
                IntegratedBudgetTarget = budgetPlanningKpis.IntegratedBudgetTarget,
                BudgetGapToIntegratedTarget = budgetPlanningKpis.BudgetGapToIntegratedTarget,
                SuggestedRestockSpend = suggestedRestockSpend,
                ActualProcurementSpend = actualProcurementSpend,
                AtRiskInventoryPlanningItems = atRiskParts.Count,
                TrendLabels = trendLabels,
                ActualRevenueTrendValues = actualRevenueTrendValues,
                BudgetTargetTrendValues = budgetTargetTrendValues,
                MonthlyBudgets = monthlyBudgets,
                SuggestedAllocations = suggestedAllocations,
                AtRiskParts = atRiskParts
                    .Take(50)
                    .Select(item => new BudgetAtRiskPartViewModel
                    {
                        ProductName = item.ProductName,
                        Category = item.Category,
                        CurrentStock = item.CurrentStock,
                        AverageDailyUnits = item.AverageDailyUnits,
                        DaysOfStockCover = item.DaysOfStockCover,
                        SuggestedRestockSpend = item.RecommendedRestockSpend
                    })
                    .ToList(),
                BudgetHistory = budgetHistory
            };
        }

        private List<FinancialBudget> LoadBudgets(int? budgetOwnerUserId)
        {
            if (!budgetOwnerUserId.HasValue)
            {
                return new List<FinancialBudget>();
            }

            return _context.FinancialBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.OwnerUserID == budgetOwnerUserId.Value &&
                    budget.Status != BudgetStatusDeleted)
                .OrderByDescending(budget => budget.UpdatedAtUtc)
                .ThenByDescending(budget => budget.BudgetID)
                .Take(400)
                .ToList();
        }

        private List<SystemLog> LoadBudgetHistoryLogs(int? budgetOwnerUserId)
        {
            if (!budgetOwnerUserId.HasValue)
            {
                return new List<SystemLog>();
            }

            return _context.SystemLogs
                .AsNoTracking()
                .Where(log =>
                    log.OwnerUserID == budgetOwnerUserId.Value &&
                    log.Action.StartsWith(BudgetHistoryLogPrefix))
                .OrderByDescending(log => log.Timestamp)
                .ThenByDescending(log => log.LogID)
                .Take(1000)
                .ToList();
        }

        private List<BudgetEvent> LoadBudgetEvents(int? budgetOwnerUserId)
        {
            if (!budgetOwnerUserId.HasValue)
            {
                return new List<BudgetEvent>();
            }

            return _context.BudgetEvents
                .AsNoTracking()
                .Where(budgetEvent => budgetEvent.OwnerUserID == budgetOwnerUserId.Value)
                .OrderByDescending(budgetEvent => budgetEvent.OccurredAtUtc)
                .ThenByDescending(budgetEvent => budgetEvent.BudgetEventID)
                .Take(2000)
                .ToList();
        }

        private static BudgetSelectionContext ResolveBudgetSelection(
            int? selectedBudgetId,
            DateTime? selectedMonthLocal,
            IReadOnlyList<FinancialBudget> allBudgets,
            bool showArchivedBudgets)
        {
            var todayLocalDate = BusinessTime.Today;
            var defaultStartLocalDate = selectedMonthLocal.HasValue
                ? ResolveMonthStart(selectedMonthLocal.Value)
                : ResolveMonthStart(todayLocalDate);
            var defaultEndLocalDate = defaultStartLocalDate.AddMonths(1).AddDays(-1);
            var monthlyBudgetsForView = ResolveMonthlyBudgetsForView(allBudgets, showArchivedBudgets);

            if (selectedBudgetId.HasValue)
            {
                var selectedBudget = allBudgets
                    .FirstOrDefault(budget => budget.BudgetID == selectedBudgetId.Value);
                if (selectedBudget == null)
                {
                    return new BudgetSelectionContext(
                        null,
                        defaultStartLocalDate,
                        defaultEndLocalDate,
                        0m,
                        $"Budget #{selectedBudgetId.Value} was not found. Select an existing budget.");
                }

                var selectedStartDate = ResolveMonthStart(selectedBudget.PeriodStartDateLocal);
                var selectedEndDate = selectedStartDate.AddMonths(1).AddDays(-1);
                var selectedMonthKey = $"{selectedStartDate:yyyy-MM}";
                var selectedBudgetForView = monthlyBudgetsForView
                    .FirstOrDefault(budget => ResolveBudgetMonthKey(budget) == selectedMonthKey);
                var effectiveSelectedBudget = selectedBudgetForView ?? selectedBudget;
                return new BudgetSelectionContext(
                    effectiveSelectedBudget,
                    selectedStartDate,
                    selectedEndDate,
                    effectiveSelectedBudget.BudgetAmount,
                    null);
            }

            if (selectedMonthLocal.HasValue)
            {
                var requestedMonthStart = ResolveMonthStart(selectedMonthLocal.Value);
                var requestedMonthEnd = requestedMonthStart.AddMonths(1).AddDays(-1);
                var requestedMonthKey = $"{requestedMonthStart:yyyy-MM}";
                var budgetForRequestedMonth = monthlyBudgetsForView
                    .FirstOrDefault(budget => ResolveBudgetMonthKey(budget) == requestedMonthKey);
                if (budgetForRequestedMonth != null)
                {
                    return new BudgetSelectionContext(
                        budgetForRequestedMonth,
                        requestedMonthStart,
                        requestedMonthEnd,
                        budgetForRequestedMonth.BudgetAmount,
                        null);
                }

                return new BudgetSelectionContext(
                    null,
                    requestedMonthStart,
                    requestedMonthEnd,
                    0m,
                    showArchivedBudgets
                        ? $"No archived budget found for {requestedMonthStart:MMMM yyyy}. Switch to active workspace to create or edit the month budget."
                        : $"No saved budget for {requestedMonthStart:MMMM yyyy} yet. Enter an amount and save to create one.");
            }

            var defaultBudget = monthlyBudgetsForView
                .OrderByDescending(budget => ResolveMonthStart(budget.PeriodStartDateLocal))
                .ThenByDescending(budget => budget.UpdatedAtUtc)
                .ThenByDescending(budget => budget.BudgetID)
                .FirstOrDefault();
            if (defaultBudget == null)
            {
                return new BudgetSelectionContext(
                    null,
                    defaultStartLocalDate,
                    defaultEndLocalDate,
                    0m,
                    null);
            }

            var defaultStartDate = ResolveMonthStart(defaultBudget.PeriodStartDateLocal);
            var defaultEndDate = defaultStartDate.AddMonths(1).AddDays(-1);
            return new BudgetSelectionContext(
                defaultBudget,
                defaultStartDate,
                defaultEndDate,
                defaultBudget.BudgetAmount,
                null);
        }

        private static List<FinancialBudget> ResolveMonthlyBudgetsForView(
            IReadOnlyList<FinancialBudget> allBudgets,
            bool showArchivedBudgets)
        {
            return allBudgets
                .Where(budget => showArchivedBudgets
                    ? budget.Status == BudgetStatusArchived
                    : budget.Status != BudgetStatusArchived &&
                      budget.Status != BudgetStatusDeleted)
                .GroupBy(ResolveBudgetMonthKey)
                .Select(group =>
                    group
                    .OrderByDescending(budget => budget.UpdatedAtUtc)
                    .ThenByDescending(budget => budget.BudgetID)
                    .First())
                .OrderByDescending(budget => ResolveMonthStart(budget.PeriodStartDateLocal))
                .ThenByDescending(budget => budget.BudgetID)
                .ToList();
        }

        private static string ResolveBudgetMonthKey(FinancialBudget budget)
        {
            var monthStart = ResolveMonthStart(budget.PeriodStartDateLocal);
            return $"{monthStart:yyyy-MM}";
        }

        private static DateTime ResolveMonthStart(DateTime date)
        {
            var normalized = date.Date;
            return new DateTime(normalized.Year, normalized.Month, 1);
        }

        private BudgetRevenueAnalytics BuildBudgetRevenueAnalytics(
            DateTime rangeStartUtc,
            DateTime rangeEndUtcInclusive,
            bool applyOwnerFilter,
            int ownerUserId)
        {
            const string budgetPaymentScope = "paid_only";
            var ordersInRange = ApplyOwnerFilter(_context.Orders.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Include(order => order.OrderDetails)
                .ThenInclude(detail => detail.Product)
                .Where(order => order.OrderDate >= rangeStartUtc && order.OrderDate <= rangeEndUtcInclusive)
                .ToList();

            var filteredOrders = ordersInRange
                .Where(order => MatchesFinancialPaymentScope(order, budgetPaymentScope))
                .ToList();

            var unitCostLookup = BuildOrderProductUnitCostLookup(
                filteredOrders.Select(order => order.OrderID),
                applyOwnerFilter,
                ownerUserId);
            var summary = BuildFinancialSummary(filteredOrders, unitCostLookup);

            return new BudgetRevenueAnalytics(filteredOrders, summary);
        }

        private static List<InventoryPlanningItemViewModel> BuildAtRiskInventoryItems(
            IEnumerable<InventoryPlanningItemViewModel> inventoryPlanningItems)
        {
            return inventoryPlanningItems
                .Where(item =>
                    string.Equals(item.StockRiskLevel, "Critical", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.StockRiskLevel, "Warning", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.DaysOfStockCover)
                .ThenByDescending(item => item.UnitsSold)
                .ToList();
        }

        private static BudgetPlanningKpis CalculateBudgetPlanningKpis(
            decimal budgetAmount,
            decimal actualProcurementSpend,
            decimal suggestedRestockSpend,
            decimal reservedAmount,
            decimal spentAmount)
        {
            var integratedBudgetTarget = Math.Round(
                actualProcurementSpend + suggestedRestockSpend,
                2,
                MidpointRounding.AwayFromZero);
            
            var totalCommitted = reservedAmount + spentAmount;
            
            var budgetGapToIntegratedTarget = integratedBudgetTarget - budgetAmount;
            var budgetVariance = budgetAmount - totalCommitted;
            var budgetUtilization = budgetAmount > 0m
                ? Math.Round((totalCommitted / budgetAmount) * 100m, 2)
                : 0m;

            return new BudgetPlanningKpis(
                integratedBudgetTarget,
                budgetGapToIntegratedTarget,
                budgetVariance,
                budgetUtilization);
        }

        private static BudgetUsageSummary BuildTotalBudgetUsage(IReadOnlyList<BudgetEvent> budgetEvents, int? selectedBudgetId)
        {
            if (!selectedBudgetId.HasValue) return new BudgetUsageSummary(0m, 0m);

            var eventsForBudget = budgetEvents.Where(e => e.BudgetID == selectedBudgetId.Value).ToList();
            var totalReserved = eventsForBudget
                .Where(e => string.Equals(e.EventType, BudgetEventTypeReserve, StringComparison.OrdinalIgnoreCase))
                .Sum(e => Math.Max(0m, e.Amount));
            var totalSpent = eventsForBudget
                .Where(e => string.Equals(e.EventType, BudgetEventTypeSpend, StringComparison.OrdinalIgnoreCase))
                .Sum(e => Math.Max(0m, e.Amount));
            var totalReleased = eventsForBudget
                .Where(e => string.Equals(e.EventType, BudgetEventTypeRelease, StringComparison.OrdinalIgnoreCase))
                .Sum(e => Math.Max(0m, e.Amount));

            var remainingReserved = Math.Round(
                Math.Max(0m, totalReserved - totalSpent - totalReleased),
                2,
                MidpointRounding.AwayFromZero);
            
            var roundedSpent = Math.Round(totalSpent, 2, MidpointRounding.AwayFromZero);
            
            return new BudgetUsageSummary(remainingReserved, roundedSpent);
        }

        private static List<BudgetAllocationSuggestionViewModel> BuildBudgetAllocationSuggestions(
            IEnumerable<InventoryPlanningItemViewModel> inventoryPlanningItems,
            decimal budgetAmount,
            IReadOnlyList<PurchaseOrderLine> currentBudgetPoLines)
        {
            var procuredByCategory = currentBudgetPoLines
                .Where(line => line.Product != null)
                .GroupBy(line => string.IsNullOrWhiteSpace(line.Product!.CategoryName) ? "Uncategorized" : line.Product.CategoryName)
                .ToDictionary(g => g.Key, g => g.Sum(line => line.LineTotal));

            var categoryPerformance = inventoryPlanningItems
                .GroupBy(item => item.Category)
                .Select(group => 
                {
                    var categoryName = string.IsNullOrWhiteSpace(group.Key) ? "Uncategorized" : group.Key;
                    var rawRestockSpend = group.Sum(item => item.RecommendedRestockSpend);
                    var procuredAmount = procuredByCategory.GetValueOrDefault(categoryName, 0m);
                    var remainingRestockSpend = Math.Max(0m, rawRestockSpend - procuredAmount);

                    return new
                    {
                        Category = categoryName,
                        UnitsSold = group.Sum(item => item.UnitsSold),
                        Revenue = group.Sum(item => item.Revenue),
                        RestockSpend = remainingRestockSpend
                    };
                })
                .OrderByDescending(item => item.RestockSpend)
                .ThenByDescending(item => item.UnitsSold)
                .ToList();

            var totalCategoryRestockSpend = categoryPerformance.Sum(item => item.RestockSpend);
            var totalCategoryRevenue = categoryPerformance.Sum(item => item.Revenue);
            var categoryCount = categoryPerformance.Count;
            
            return categoryPerformance
                .Select(item =>
                {
                    decimal allocationShare = 0m;
                    if (totalCategoryRestockSpend > 0m)
                    {
                        allocationShare = item.RestockSpend / totalCategoryRestockSpend;
                    }
                    else if (totalCategoryRevenue > 0m)
                    {
                        allocationShare = item.Revenue / totalCategoryRevenue;
                    }
                    else if (categoryCount > 0)
                    {
                        allocationShare = 1m / categoryCount;
                    }

                    var allocationSharePercentage = Math.Round(
                        allocationShare * 100m,
                        2,
                        MidpointRounding.AwayFromZero);
                        
                    var suggestedBudgetAmount = Math.Round(
                        budgetAmount * allocationShare,
                        2,
                        MidpointRounding.AwayFromZero);

                    return new BudgetAllocationSuggestionViewModel
                    {
                        Category = item.Category,
                        UnitsSold = item.UnitsSold,
                        RevenueSharePercentage = allocationSharePercentage,
                        SuggestedBudgetAmount = suggestedBudgetAmount,
                        SuggestedRestockSpend = Math.Round(item.RestockSpend, 2, MidpointRounding.AwayFromZero)
                    };
                })
                .ToList();
        }

        private static List<BudgetMonthRowViewModel> BuildMonthlyBudgetRows(
            IReadOnlyList<FinancialBudget> allBudgets,
            int? selectedBudgetId,
            bool showArchivedBudgets,
            IReadOnlyDictionary<int, List<BudgetHistoryItemViewModel>> budgetHistoryLookupByBudgetId,
            IReadOnlyDictionary<int, BudgetUsageSummary> budgetUsageLookupByBudgetId)
        {
            var selectedMonthKey = selectedBudgetId.HasValue
                ? allBudgets
                    .Where(budget => budget.BudgetID == selectedBudgetId.Value)
                    .Select(ResolveBudgetMonthKey)
                    .FirstOrDefault()
                : null;
            var monthlyBudgetsForView = ResolveMonthlyBudgetsForView(allBudgets, showArchivedBudgets);

            return monthlyBudgetsForView
                .Select(budget =>
                {
                    var monthStart = ResolveMonthStart(budget.PeriodStartDateLocal);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    var usageSummary = budgetUsageLookupByBudgetId.TryGetValue(budget.BudgetID, out var usageForBudget)
                        ? usageForBudget
                        : new BudgetUsageSummary(0m, 0m);
                    return new BudgetMonthRowViewModel
                    {
                        BudgetId = budget.BudgetID,
                        MonthStartDateLocal = monthStart,
                        MonthEndDateLocal = monthEnd,
                        BudgetAmount = budget.BudgetAmount,
                        Status = budget.Status,
                        IsSelected = !string.IsNullOrWhiteSpace(selectedMonthKey)
                            ? ResolveBudgetMonthKey(budget) == selectedMonthKey
                            : selectedBudgetId.HasValue && selectedBudgetId.Value == budget.BudgetID,
                        UpdatedAtUtc = budget.UpdatedAtUtc,
                        ReservedAmount = usageSummary.ReservedAmount,
                        SpentAmount = usageSummary.SpentAmount,
                        UsageStatus = ResolveBudgetUsageStatus(usageSummary),
                        History = budgetHistoryLookupByBudgetId.TryGetValue(budget.BudgetID, out var historyForBudget)
                            ? historyForBudget
                            : new List<BudgetHistoryItemViewModel>()
                    };
                })
                .OrderByDescending(row => row.MonthStartDateLocal)
                .ToList();
        }

        private static string ResolveBudgetUsageStatus(BudgetUsageSummary usageSummary)
        {
            if (usageSummary.SpentAmount > 0m && usageSummary.ReservedAmount > 0m)
            {
                return "Partially Spent";
            }

            if (usageSummary.SpentAmount > 0m)
            {
                return "Spent";
            }

            if (usageSummary.ReservedAmount > 0m)
            {
                return "Reserved";
            }

            return "Unused";
        }

        private static Dictionary<int, List<BudgetHistoryItemViewModel>> BuildBudgetHistoryLookupByBudgetId(
            IReadOnlyList<SystemLog> budgetHistoryLogs)
        {
            var parsedLogs = budgetHistoryLogs
                .Select(log =>
                {
                    var parsed = TryExtractBudgetIdFromHistoryAction(log.Action, out var budgetId);
                    return new
                    {
                        Log = log,
                        BudgetId = parsed ? budgetId : (int?)null
                    };
                })
                .Where(item => item.BudgetId.HasValue)
                .ToList();

            return parsedLogs
                .GroupBy(item => item.BudgetId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item => item.Log.Timestamp)
                        .ThenByDescending(item => item.Log.LogID)
                        .Take(50)
                        .Select(item => new BudgetHistoryItemViewModel
                        {
                            BudgetId = group.Key,
                            Action = item.Log.Action,
                            ChangedAtUtc = item.Log.Timestamp,
                            ChangedAtLabel = BusinessTime.ConvertUtcToBusinessTime(item.Log.Timestamp).ToString("MMM dd, yyyy HH:mm")
                        })
                        .ToList());
        }

        private static List<BudgetHistoryItemViewModel> BuildBudgetHistoryForBudget(
            int budgetId,
            IReadOnlyDictionary<int, List<BudgetHistoryItemViewModel>> budgetHistoryLookupByBudgetId)
        {
            return budgetHistoryLookupByBudgetId.TryGetValue(budgetId, out var historyForBudget)
                ? historyForBudget
                : new List<BudgetHistoryItemViewModel>();
        }

        private static Dictionary<int, List<BudgetHistoryItemViewModel>> BuildBudgetHistoryLookupByBudgetIdFromEvents(
            IReadOnlyList<BudgetEvent> budgetEvents)
        {
            return budgetEvents
                .GroupBy(budgetEvent => budgetEvent.BudgetID)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(budgetEvent => budgetEvent.OccurredAtUtc)
                        .ThenByDescending(budgetEvent => budgetEvent.BudgetEventID)
                        .Take(100)
                        .Select(budgetEvent => new BudgetHistoryItemViewModel
                        {
                            BudgetId = group.Key,
                            Action = FormatBudgetEventAction(budgetEvent),
                            ChangedAtUtc = budgetEvent.OccurredAtUtc,
                            ChangedAtLabel = BusinessTime.ConvertUtcToBusinessTime(budgetEvent.OccurredAtUtc)
                                .ToString("MMM dd, yyyy HH:mm")
                        })
                        .ToList());
        }

        private static string FormatBudgetEventAction(BudgetEvent budgetEvent)
        {
            var normalizedReason = string.IsNullOrWhiteSpace(budgetEvent.Reason)
                ? "No reason provided."
                : budgetEvent.Reason.Trim();
            var referenceSuffix = !string.IsNullOrWhiteSpace(budgetEvent.ReferenceId)
                ? $" ({budgetEvent.ReferenceType} {budgetEvent.ReferenceId})"
                : string.Empty;
            return budgetEvent.EventType switch
            {
                BudgetEventTypeCreate =>
                    $"Created budget at {budgetEvent.AfterAmount.GetValueOrDefault(budgetEvent.Amount):C}. Reason: {normalizedReason}.",
                BudgetEventTypeUpdate =>
                    $"Updated budget {budgetEvent.BeforeAmount.GetValueOrDefault():C} -> {budgetEvent.AfterAmount.GetValueOrDefault():C}. Reason: {normalizedReason}.",
                BudgetEventTypeArchive =>
                    $"Archived budget. Reason: {normalizedReason}.",
                BudgetEventTypeRestore =>
                    $"Restored budget to active. Reason: {normalizedReason}.",
                BudgetEventTypeReserve =>
                    $"Reserved {budgetEvent.Amount:C}{referenceSuffix}. Reason: {normalizedReason}.",
                BudgetEventTypeSpend =>
                    $"Spent {budgetEvent.Amount:C}{referenceSuffix}. Reason: {normalizedReason}.",
                BudgetEventTypeRelease =>
                    $"Released {budgetEvent.Amount:C}{referenceSuffix}. Reason: {normalizedReason}.",
                _ =>
                    $"{budgetEvent.EventType}: {budgetEvent.Amount:C}{referenceSuffix}. Reason: {normalizedReason}."
            };
        }

        private static Dictionary<int, List<BudgetHistoryItemViewModel>> MergeBudgetHistoryLookups(
            IReadOnlyDictionary<int, List<BudgetHistoryItemViewModel>> firstLookup,
            IReadOnlyDictionary<int, List<BudgetHistoryItemViewModel>> secondLookup)
        {
            var mergedLookup = new Dictionary<int, List<BudgetHistoryItemViewModel>>();
            var budgetIds = firstLookup.Keys
                .Union(secondLookup.Keys)
                .Distinct()
                .ToList();

            foreach (var budgetId in budgetIds)
            {
                var firstItems = firstLookup.TryGetValue(budgetId, out var firstHistory)
                    ? firstHistory
                    : new List<BudgetHistoryItemViewModel>();
                var secondItems = secondLookup.TryGetValue(budgetId, out var secondHistory)
                    ? secondHistory
                    : new List<BudgetHistoryItemViewModel>();

                mergedLookup[budgetId] = firstItems
                    .Concat(secondItems)
                    .OrderByDescending(item => item.ChangedAtUtc)
                    .Take(100)
                    .ToList();
            }

            return mergedLookup;
        }

        private static Dictionary<int, BudgetUsageSummary> BuildBudgetUsageLookupByBudgetId(
            IReadOnlyList<BudgetEvent> budgetEvents)
        {
            return budgetEvents
                .GroupBy(budgetEvent => budgetEvent.BudgetID)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var totalReserved = group
                            .Where(budgetEvent => string.Equals(budgetEvent.EventType, BudgetEventTypeReserve, StringComparison.OrdinalIgnoreCase))
                            .Sum(budgetEvent => Math.Max(0m, budgetEvent.Amount));
                        var totalSpent = group
                            .Where(budgetEvent => string.Equals(budgetEvent.EventType, BudgetEventTypeSpend, StringComparison.OrdinalIgnoreCase))
                            .Sum(budgetEvent => Math.Max(0m, budgetEvent.Amount));
                        var totalReleased = group
                            .Where(budgetEvent => string.Equals(budgetEvent.EventType, BudgetEventTypeRelease, StringComparison.OrdinalIgnoreCase))
                            .Sum(budgetEvent => Math.Max(0m, budgetEvent.Amount));

                        var remainingReserved = Math.Round(
                            Math.Max(0m, totalReserved - totalSpent - totalReleased),
                            2,
                            MidpointRounding.AwayFromZero);
                        var roundedSpent = Math.Round(totalSpent, 2, MidpointRounding.AwayFromZero);
                        return new BudgetUsageSummary(remainingReserved, roundedSpent);
                    });
        }

        private static bool TryExtractBudgetIdFromHistoryAction(
            string? action,
            out int budgetId)
        {
            budgetId = 0;
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var markerIndex = action.IndexOf(BudgetHistoryBudgetTagPrefix, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            var valueStartIndex = markerIndex + BudgetHistoryBudgetTagPrefix.Length;
            var valueEndIndex = action.IndexOf(']', valueStartIndex);
            if (valueEndIndex <= valueStartIndex)
            {
                return false;
            }

            return int.TryParse(action[valueStartIndex..valueEndIndex], out budgetId) && budgetId > 0;
        }

        public BusinessIntelligenceViewModel GetBusinessIntelligence(
            DateTime startDateUtc,
            DateTime endDateUtc,
            string adjustmentFilter = "all")
        {
            var normalizedAdjustmentFilter = NormalizeAdjustmentFilter(adjustmentFilter);
            var (rangeStartLocalDate, rangeEndLocalDate) = NormalizeLocalDateRange(startDateUtc, endDateUtc);
            var (rangeStartUtc, rangeEndUtcInclusive) = NormalizeRange(startDateUtc, endDateUtc);
            var (applyOwnerFilter, ownerUserId) = ResolveOwnerFilterContext();

            var products = ApplyOwnerFilter(_context.Products.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(product => product.MarketPrice > 0)
                .ToList();
            var daysInRange = Math.Max(1, (rangeEndLocalDate - rangeStartLocalDate).Days + 1);
            var monthlyProjectionFactor = 30m / daysInRange;
            var unitsSoldOrderDetailsInRange = ApplyOwnerFilter(_context.OrderDetails.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(detail =>
                    detail.Order != null &&
                    detail.Order.OrderDate >= rangeStartUtc &&
                    detail.Order.OrderDate <= rangeEndUtcInclusive);
            var unitsSoldByProduct = RevenueRecognitionPolicy
                .ApplyRecognizedRevenueOrderDetailFilter(unitsSoldOrderDetailsInRange)
                .GroupBy(detail => detail.ProductID)
                .Select(group => new
                {
                    ProductId = group.Key,
                    UnitsSold = group.Sum(detail => detail.Quantity)
                })
                .ToDictionary(item => item.ProductId, item => item.UnitsSold);

            var competitorAnalysis = products
                .Select(product =>
                {
                    var variancePercent = ((product.SellingPrice - product.MarketPrice) / product.MarketPrice) * 100m;
                    var roundedVariance = Math.Round(Math.Abs(variancePercent), 1);
                    var adjustmentType = Math.Abs(variancePercent) switch
                    {
                        <= 2m => "Maintain",
                        _ when variancePercent > 0 => "Decrease",
                        _ => "Increase"
                    };

                    var suggestion = adjustmentType switch
                    {
                        "Decrease" => $"Decrease by ~{roundedVariance:0.#}%",
                        "Increase" => $"Increase by ~{roundedVariance:0.#}%",
                        _ => "Maintain current price"
                    };
                    unitsSoldByProduct.TryGetValue(product.ProductID, out var unitsSoldInRange);
                    var estimatedMonthlyUnits = Math.Round(
                        unitsSoldInRange * monthlyProjectionFactor,
                        1,
                        MidpointRounding.AwayFromZero);
                    var suggestedPrice = adjustmentType == "Maintain"
                        ? product.SellingPrice
                        : product.MarketPrice;
                    var estimatedRevenueImpact = CalculateEstimatedRevenueImpact(
                        product.SellingPrice,
                        suggestedPrice,
                        estimatedMonthlyUnits);

                    return new CompetitorProductViewModel
                    {
                        ProductId = product.ProductID,
                        ProductName = product.ProductName,
                        Category = product.CategoryName ?? "General",
                        StorePrice = product.SellingPrice,
                        MarketAveragePrice = product.MarketPrice,
                        CompetitorLowPrice = Math.Round(product.MarketPrice * 0.97m, 2),
                        CompetitorHighPrice = Math.Round(product.MarketPrice * 1.03m, 2),
                        SuggestedPrice = Math.Round(suggestedPrice, 2, MidpointRounding.AwayFromZero),
                        SuggestedAdjustment = suggestion,
                        AdjustmentType = adjustmentType,
                        EstimatedMonthlyUnits = estimatedMonthlyUnits,
                        EstimatedRevenueImpact = estimatedRevenueImpact
                    };
                })
                .Where(item =>
                    normalizedAdjustmentFilter == "all" ||
                    string.Equals(item.AdjustmentType, normalizedAdjustmentFilter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => Math.Abs(item.StorePrice - item.MarketAveragePrice))
                .Take(25)
                .ToList();

            var stockMovements = ApplyOwnerFilter(_context.InventoryMovements.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(movement =>
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtcInclusive)
                .ToList();

            var outstandingLoyaltyPoints = ApplyOwnerFilter(_context.Customers.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Sum(customer => customer.LoyaltyPoints);

            return new BusinessIntelligenceViewModel
            {
                SelectedAdjustmentFilter = normalizedAdjustmentFilter,
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                CompetitorProducts = competitorAnalysis,
                StockInMovementsInRange = stockMovements
                    .Where(movement => movement.MovementType == "StockIn")
                    .Sum(movement => Math.Max(0, movement.QuantityDelta)),
                StockOutMovementsInRange = stockMovements
                    .Where(movement => movement.MovementType == "StockOut")
                    .Sum(movement => Math.Abs(movement.QuantityDelta)),
                OutstandingLoyaltyPoints = outstandingLoyaltyPoints,
                LoyaltyLiability = _loyaltyService.EstimateLiability(outstandingLoyaltyPoints)
            };
        }

        public BudgetSaveResult SaveFinancialBudget(
            int? selectedBudgetId,
            DateTime budgetMonthLocal,
            decimal budgetAmount,
            string? changeReason)
        {
            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                return new BudgetSaveResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var normalizedStartDate = ResolveMonthStart(budgetMonthLocal);
            var normalizedEndDateInclusive = normalizedStartDate.AddMonths(1).AddDays(-1);
            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new BudgetSaveResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var boundedBudgetAmount = budgetAmount < 0m ? 0m : Math.Round(budgetAmount, 2);
            var normalizedReason = string.IsNullOrWhiteSpace(changeReason)
                ? string.Empty
                : changeReason.Trim();
            var ownerBudgetQuery = _context.FinancialBudgets
                .Where(budget =>
                    budget.OwnerUserID == ownerUserId.Value &&
                    budget.Status != BudgetStatusDeleted);

            var selectedBudget = selectedBudgetId.HasValue
                ? ownerBudgetQuery.FirstOrDefault(budget => budget.BudgetID == selectedBudgetId.Value)
                : null;
            if (selectedBudgetId.HasValue && selectedBudget == null)
            {
                return new BudgetSaveResult
                {
                    Succeeded = false,
                    Message = $"Budget #{selectedBudgetId.Value} was not found."
                };
            }

            var nowUtc = DateTime.UtcNow;
            var monthlyBudgets = ownerBudgetQuery
                .Where(budget =>
                    budget.PeriodStartDateLocal.Year == normalizedStartDate.Year &&
                    budget.PeriodStartDateLocal.Month == normalizedStartDate.Month)
                .OrderByDescending(budget => budget.UpdatedAtUtc)
                .ThenByDescending(budget => budget.BudgetID)
                .ToList();

            var targetBudget = monthlyBudgets.FirstOrDefault();
            if (targetBudget == null)
            {
                var createReason = string.IsNullOrWhiteSpace(normalizedReason)
                    ? "Initial monthly budget created."
                    : normalizedReason;
                targetBudget = new FinancialBudget
                {
                    OwnerUserID = ownerUserId.Value,
                    Status = BudgetStatusActive,
                    PeriodStartDateLocal = normalizedStartDate,
                    PeriodEndDateLocal = normalizedEndDateInclusive,
                    BudgetAmount = boundedBudgetAmount,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };
                _context.FinancialBudgets.Add(targetBudget);
                _context.SaveChanges();

                AddBudgetHistoryLog(
                    ownerUserId.Value,
                    $"Budget Create {BuildBudgetHistoryContextTag(targetBudget.BudgetID, normalizedStartDate)} Amt:{boundedBudgetAmount:0.00}");
                AddBudgetEvent(
                    ownerUserId.Value,
                    targetBudget.BudgetID,
                    BudgetEventTypeCreate,
                    boundedBudgetAmount,
                    null,
                    boundedBudgetAmount,
                    createReason,
                    BudgetEventReferenceTypeBudget,
                    targetBudget.BudgetID.ToString(CultureInfo.InvariantCulture),
                    nowUtc);
                _context.SaveChanges();
                return new BudgetSaveResult
                {
                    Succeeded = true,
                    BudgetId = targetBudget.BudgetID,
                    Message = "Monthly budget saved."
                };
            }

            var previousAmount = targetBudget.BudgetAmount;
            var amountChanged = previousAmount != boundedBudgetAmount;
            if (amountChanged && string.IsNullOrWhiteSpace(normalizedReason))
            {
                return new BudgetSaveResult
                {
                    Succeeded = false,
                    BudgetId = targetBudget.BudgetID,
                    Message = "Enter a reason before updating an existing budget."
                };
            }

            targetBudget.BudgetAmount = boundedBudgetAmount;
            targetBudget.Status = BudgetStatusActive;
            targetBudget.PeriodStartDateLocal = normalizedStartDate;
            targetBudget.PeriodEndDateLocal = normalizedEndDateInclusive;
            targetBudget.UpdatedAtUtc = nowUtc;

            foreach (var duplicateBudget in monthlyBudgets.Skip(1))
            {
                duplicateBudget.Status = BudgetStatusArchived;
                duplicateBudget.UpdatedAtUtc = nowUtc;
            }

            _context.SaveChanges();

            if (amountChanged)
            {
                AddBudgetHistoryLog(
                    ownerUserId.Value,
                    $"Budget Update {BuildBudgetHistoryContextTag(targetBudget.BudgetID, normalizedStartDate)} Amt:{previousAmount:0.00}->{boundedBudgetAmount:0.00}");
                AddBudgetEvent(
                    ownerUserId.Value,
                    targetBudget.BudgetID,
                    BudgetEventTypeUpdate,
                    Math.Round(boundedBudgetAmount - previousAmount, 2, MidpointRounding.AwayFromZero),
                    previousAmount,
                    boundedBudgetAmount,
                    normalizedReason,
                    BudgetEventReferenceTypeBudget,
                    targetBudget.BudgetID.ToString(CultureInfo.InvariantCulture),
                    nowUtc);
                _context.SaveChanges();
            }

            return new BudgetSaveResult
            {
                Succeeded = true,
                BudgetId = targetBudget.BudgetID,
                Message = amountChanged
                    ? "Monthly budget updated."
                    : "No budget amount change detected."
            };
        }

        public bool TryArchiveFinancialBudget(int budgetId, out string message)
        {
            message = "Unable to archive budget.";
            if (budgetId <= 0)
            {
                message = "Budget record was not found.";
                return false;
            }

            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                message = "Select an owner workspace with edit permission before making changes.";
                return false;
            }

            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                message = "Select an owner workspace with edit permission before making changes.";
                return false;
            }

            var ownerBudgetQuery = _context.FinancialBudgets
                .Where(budget =>
                    budget.OwnerUserID == ownerUserId.Value &&
                    budget.Status != BudgetStatusDeleted)
                .AsQueryable();
            var selectedBudget = ownerBudgetQuery
                .FirstOrDefault(budget => budget.BudgetID == budgetId);
            if (selectedBudget == null)
            {
                message = $"Budget #{budgetId} was not found.";
                return false;
            }

            var monthStart = ResolveMonthStart(selectedBudget.PeriodStartDateLocal);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthlyBudgets = ownerBudgetQuery
                .Where(budget =>
                    budget.PeriodStartDateLocal.Year == monthStart.Year &&
                    budget.PeriodStartDateLocal.Month == monthStart.Month)
                .ToList();
            if (!monthlyBudgets.Any())
            {
                message = "No monthly budget found to archive.";
                return false;
            }

            var monthlyBudgetIds = monthlyBudgets
                .Select(budget => budget.BudgetID)
                .ToList();
            var hasLinkedProcurements = HasLinkedApprovedProcurementsForBudgets(
                ownerUserId.Value,
                monthlyBudgetIds);
            if (hasLinkedProcurements)
            {
                message = $"Cannot archive {monthStart:MMMM yyyy} budget because it is linked to approved or received procurements.";
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            foreach (var budget in monthlyBudgets)
            {
                budget.Status = BudgetStatusArchived;
                budget.UpdatedAtUtc = nowUtc;
            }

            _context.SaveChanges();
            AddBudgetHistoryLog(
                ownerUserId.Value,
                $"Budget Archive {BuildBudgetHistoryContextTag(selectedBudget.BudgetID, monthStart)} Amt:{selectedBudget.BudgetAmount:0.00}");
            AddBudgetEvent(
                ownerUserId.Value,
                selectedBudget.BudgetID,
                BudgetEventTypeArchive,
                0m,
                selectedBudget.BudgetAmount,
                selectedBudget.BudgetAmount,
                "Budget was archived.",
                BudgetEventReferenceTypeBudget,
                selectedBudget.BudgetID.ToString(CultureInfo.InvariantCulture),
                nowUtc);
            _context.SaveChanges();
            message = $"Budget for {monthStart:MMMM yyyy} was archived.";
            return true;
        }

        public bool TryRestoreFinancialBudget(int budgetId, out string message)
        {
            message = "Unable to restore budget.";
            if (budgetId <= 0)
            {
                message = "Budget record was not found.";
                return false;
            }

            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                message = "Select an owner workspace with edit permission before making changes.";
                return false;
            }

            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                message = "Select an owner workspace with edit permission before making changes.";
                return false;
            }

            var ownerBudgetQuery = _context.FinancialBudgets
                .Where(budget =>
                    budget.OwnerUserID == ownerUserId.Value &&
                    budget.Status != BudgetStatusDeleted)
                .AsQueryable();
            var selectedBudget = ownerBudgetQuery
                .FirstOrDefault(budget => budget.BudgetID == budgetId);
            if (selectedBudget == null)
            {
                message = $"Budget #{budgetId} was not found.";
                return false;
            }

            var monthStart = ResolveMonthStart(selectedBudget.PeriodStartDateLocal);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            if (!string.Equals(selectedBudget.Status, BudgetStatusArchived, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Budget for {monthStart:MMMM yyyy} is already active.";
                return false;
            }

            var monthlyBudgets = ownerBudgetQuery
                .Where(budget =>
                    budget.PeriodStartDateLocal.Year == monthStart.Year &&
                    budget.PeriodStartDateLocal.Month == monthStart.Month)
                .ToList();
            if (!monthlyBudgets.Any())
            {
                message = "No monthly budget found to restore.";
                return false;
            }

            var hasAnotherActiveBudget = monthlyBudgets.Any(budget =>
                budget.BudgetID != selectedBudget.BudgetID &&
                string.Equals(budget.Status, BudgetStatusActive, StringComparison.OrdinalIgnoreCase));
            if (hasAnotherActiveBudget)
            {
                message = $"Cannot restore {monthStart:MMMM yyyy} budget because an active budget already exists for the same month.";
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            selectedBudget.Status = BudgetStatusActive;
            selectedBudget.PeriodStartDateLocal = monthStart;
            selectedBudget.PeriodEndDateLocal = monthEnd;
            selectedBudget.UpdatedAtUtc = nowUtc;

            foreach (var duplicateBudget in monthlyBudgets.Where(budget => budget.BudgetID != selectedBudget.BudgetID))
            {
                duplicateBudget.Status = BudgetStatusArchived;
                duplicateBudget.UpdatedAtUtc = nowUtc;
            }

            _context.SaveChanges();
            AddBudgetHistoryLog(
                ownerUserId.Value,
                $"Budget Restore {BuildBudgetHistoryContextTag(selectedBudget.BudgetID, monthStart)} Amt:{selectedBudget.BudgetAmount:0.00}");
            AddBudgetEvent(
                ownerUserId.Value,
                selectedBudget.BudgetID,
                BudgetEventTypeRestore,
                0m,
                selectedBudget.BudgetAmount,
                selectedBudget.BudgetAmount,
                "Budget was restored from archive.",
                BudgetEventReferenceTypeBudget,
                selectedBudget.BudgetID.ToString(CultureInfo.InvariantCulture),
                nowUtc);
            _context.SaveChanges();

            message = $"Budget for {monthStart:MMMM yyyy} was restored.";
            return true;
        }

        private decimal ResolveBudgetAmount(DateTime rangeStartLocalDate, DateTime rangeEndLocalDate)
        {
            var budgetOwnerUserId = ResolveWritableOwnerUserId();
            if (!budgetOwnerUserId.HasValue)
            {
                return 0m;
            }

            var (rangeStartDate, rangeEndDate) = NormalizeLocalDateRange(rangeStartLocalDate, rangeEndLocalDate);
            var activeBudgets = _context.FinancialBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.OwnerUserID == budgetOwnerUserId.Value &&
                    budget.Status == BudgetStatusActive)
                .ToList()
                .GroupBy(ResolveBudgetMonthKey)
                .Select(group =>
                    group
                    .OrderByDescending(budget => budget.UpdatedAtUtc)
                    .ThenByDescending(budget => budget.BudgetID)
                    .First())
                .ToList();

            var exactRangeAmount = activeBudgets
                .Where(budget =>
                    budget.PeriodStartDateLocal == rangeStartDate &&
                    budget.PeriodEndDateLocal == rangeEndDate)
                .Select(budget => (decimal?)budget.BudgetAmount)
                .FirstOrDefault();

            if (exactRangeAmount.HasValue)
            {
                return exactRangeAmount.Value;
            }

            var overlappingBudgets = activeBudgets
                .Where(budget =>
                    budget.PeriodStartDateLocal <= rangeEndDate &&
                    budget.PeriodEndDateLocal >= rangeStartDate)
                .ToList();

            if (!overlappingBudgets.Any())
            {
                return 0m;
            }

            var proratedBudgetAmount = 0m;
            foreach (var budget in overlappingBudgets)
            {
                var budgetStart = budget.PeriodStartDateLocal.Date;
                var budgetEnd = budget.PeriodEndDateLocal.Date;
                if (budgetEnd < budgetStart)
                {
                    continue;
                }

                var overlapStart = budgetStart > rangeStartDate ? budgetStart : rangeStartDate;
                var overlapEnd = budgetEnd < rangeEndDate ? budgetEnd : rangeEndDate;
                if (overlapEnd < overlapStart)
                {
                    continue;
                }

                var budgetPeriodDays = (budgetEnd - budgetStart).Days + 1;
                var overlapDays = (overlapEnd - overlapStart).Days + 1;
                if (budgetPeriodDays <= 0 || overlapDays <= 0)
                {
                    continue;
                }

                proratedBudgetAmount += budget.BudgetAmount * (overlapDays / (decimal)budgetPeriodDays);
            }

            return Math.Round(proratedBudgetAmount, 2, MidpointRounding.AwayFromZero);
        }

        public bool TryUpdateProductPrice(int productId, decimal newPrice)
        {
            if (productId <= 0 || newPrice <= 0m)
            {
                return false;
            }

            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                return false;
            }

            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return false;
            }

            var boundedPrice = Math.Round(newPrice, 2, MidpointRounding.AwayFromZero);
            var product = _context.Products.FirstOrDefault(product =>
                product.ProductID == productId &&
                product.OwnerUserID == ownerUserId.Value &&
                product.MarketPriceSource != ArchivedMarketPriceSource);

            if (product == null)
            {
                return false;
            }

            product.SellingPrice = boundedPrice;
            _context.SaveChanges();
            return true;
        }

        private List<InventoryPlanningItemViewModel> BuildInventoryPlanningInsights(
            DateTime rangeStartUtc,
            DateTime rangeEndUtcInclusive,
            int daysInRange,
            bool applyOwnerFilter,
            int ownerUserId)
        {
            var detailsInRange = ApplyOwnerFilter(_context.OrderDetails.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Include(detail => detail.Order)
                .Where(detail =>
                    detail.Order != null &&
                    detail.ProductID > 0 &&
                    detail.Order.OrderDate >= rangeStartUtc &&
                    detail.Order.OrderDate <= rangeEndUtcInclusive &&
                    detail.Order.PaymentStatus == RevenueRecognitionPolicy.PaidPaymentStatus &&
                    detail.Order.PaymentStatus != RevenueRecognitionPolicy.RefundedPaymentStatus &&
                    detail.Order.OrderStatus != RevenueRecognitionPolicy.CancelledOrderStatus)
                .ToList();

            var unitCostLookup = BuildOrderProductUnitCostLookup(
                detailsInRange.Select(detail => detail.OrderID),
                applyOwnerFilter,
                ownerUserId);

            var allProducts = ApplyOwnerFilter(_context.Products.AsNoTracking(), applyOwnerFilter, ownerUserId).ToList();
            var detailsByProduct = detailsInRange
                .GroupBy(detail => detail.ProductID)
                .ToDictionary(g => g.Key, g => g.ToList());

            return allProducts.Select(product =>
            {
                var productDetails = detailsByProduct.GetValueOrDefault(product.ProductID, new List<OrderDetail>());
                var unitsSold = productDetails.Sum(detail => detail.Quantity);
                var averageDailyUnitsRaw = unitsSold / (decimal)Math.Max(1, daysInRange);
                var averageDailyUnits = Math.Round(averageDailyUnitsRaw, 2, MidpointRounding.AwayFromZero);
                
                decimal daysOfStockCover = 999m;
                if (product.StockQuantity <= 0)
                {
                    daysOfStockCover = 0m;
                }
                else if (averageDailyUnitsRaw > 0m)
                {
                    daysOfStockCover = Math.Round(product.StockQuantity / averageDailyUnitsRaw, 1, MidpointRounding.AwayFromZero);
                }

                var stockRiskLevel = DetermineStockRisk(daysOfStockCover);
                
                var targetStockForThirtyDays = averageDailyUnitsRaw > 0m 
                    ? (int)Math.Ceiling(averageDailyUnitsRaw * 30m) 
                    : (product.StockQuantity <= 0 ? 5 : 0);

                var recommendedRestockUnits = Math.Max(0, targetStockForThirtyDays - product.StockQuantity);
                var recommendedRestockSpend = Math.Round(
                    recommendedRestockUnits * product.CostPrice,
                    2,
                    MidpointRounding.AwayFromZero);

                return new InventoryPlanningItemViewModel
                {
                    ProductId = product.ProductID,
                    ProductName = product.ProductName,
                    Category = string.IsNullOrWhiteSpace(product.CategoryName) ? "Uncategorized" : product.CategoryName,
                    UnitsSold = unitsSold,
                    OrdersCount = productDetails.Select(detail => detail.OrderID).Distinct().Count(),
                    Revenue = productDetails.Sum(detail => detail.SubTotal),
                    GrossProfit = productDetails.Sum(detail =>
                        detail.SubTotal - (detail.Quantity * ResolveUnitCost(detail, unitCostLookup))),
                    CurrentStock = product.StockQuantity,
                    AverageDailyUnits = averageDailyUnits,
                    DaysOfStockCover = daysOfStockCover,
                    StockRiskLevel = stockRiskLevel,
                    RecommendedRestockUnits = recommendedRestockUnits,
                    RecommendedRestockSpend = recommendedRestockSpend,
                    LastSoldAtUtc = productDetails.Any() ? productDetails.Max(detail => detail.Order!.OrderDate) : DateTime.MinValue
                };
            })
            .ToList();
        }

        private Dictionary<(int OrderId, int ProductId), decimal> BuildOrderProductUnitCostLookup(
            IEnumerable<int> orderIds,
            bool applyOwnerFilter,
            int ownerUserId)
        {
            var normalizedOrderIds = orderIds
                .Where(orderId => orderId > 0)
                .Distinct()
                .ToList();
            if (!normalizedOrderIds.Any())
            {
                return new Dictionary<(int OrderId, int ProductId), decimal>();
            }

            var orderReferenceIds = normalizedOrderIds
                .Select(orderId => orderId.ToString())
                .ToList();

            var orderMovements = ApplyOwnerFilter(_context.InventoryMovements.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(movement =>
                    movement.ReferenceType == "Order" &&
                    movement.QuantityDelta < 0 &&
                    orderReferenceIds.Contains(movement.ReferenceId))
                .ToList();

            return orderMovements
                .Select(movement =>
                {
                    var parsed = int.TryParse(movement.ReferenceId, out var orderId);
                    var units = Math.Abs(movement.QuantityDelta);
                    return new
                    {
                        IsValid = parsed && units > 0,
                        OrderId = orderId,
                        movement.ProductID,
                        Units = units,
                        TotalCost = movement.UnitCostAtMovement * units
                    };
                })
                .Where(item => item.IsValid)
                .GroupBy(item => (item.OrderId, item.ProductID))
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var totalUnits = group.Sum(item => item.Units);
                        var totalCost = group.Sum(item => item.TotalCost);
                        return totalUnits > 0
                            ? Math.Round(totalCost / totalUnits, 4, MidpointRounding.AwayFromZero)
                            : 0m;
                    });
        }

        private decimal CalculateProcurementSpend(
            DateTime rangeStartUtc,
            DateTime rangeEndUtcInclusive,
            bool applyOwnerFilter,
            int ownerUserId)
        {
            var procurementSpend = ApplyOwnerFilter(_context.InventoryMovements.AsNoTracking(), applyOwnerFilter, ownerUserId)
                .Where(movement =>
                    movement.MovementType == "StockIn" &&
                    (movement.ReferenceType == "Procurement" || movement.ReferenceType == "PurchaseOrder") &&
                    movement.QuantityDelta > 0 &&
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtcInclusive)
                .Select(movement => movement.QuantityDelta * movement.UnitCostAtMovement)
                .Sum();

            return Math.Round(procurementSpend, 2, MidpointRounding.AwayFromZero);
        }

        private bool HasPurchaseOrderTables()
        {
            if (_hasPurchaseOrderTables.HasValue)
            {
                return _hasPurchaseOrderTables.Value;
            }

            if (!_context.Database.IsRelational())
            {
                _hasPurchaseOrderTables = true;
                return true;
            }

            var connection = _context.Database.GetDbConnection();
            var wasClosed = connection.State == ConnectionState.Closed;
            try
            {
                if (wasClosed)
                {
                    connection.Open();
                }

                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(1)
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('PurchaseOrders', 'PurchaseOrderLines')";

                var tableCount = Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
                _hasPurchaseOrderTables = tableCount == 2;
            }
            catch
            {
                _hasPurchaseOrderTables = false;
            }
            finally
            {
                if (wasClosed && connection.State != ConnectionState.Closed)
                {
                    connection.Close();
                }
            }

            return _hasPurchaseOrderTables.Value;
        }

        private bool HasLinkedApprovedProcurementsForBudgets(int ownerUserId, List<int> budgetIds)
        {
            if (budgetIds.Count == 0)
            {
                return false;
            }

            if (HasPurchaseOrderTables())
            {
                var hasLinkedPurchaseOrders = _context.PurchaseOrders
                    .AsNoTracking()
                    .Any(purchaseOrder =>
                        purchaseOrder.OwnerUserID == ownerUserId &&
                        purchaseOrder.BudgetID.HasValue &&
                        budgetIds.Contains(purchaseOrder.BudgetID.Value) &&
                        (purchaseOrder.Status == PurchaseOrderStatusApproved ||
                         purchaseOrder.Status == PurchaseOrderStatusPartiallyReceived ||
                         purchaseOrder.Status == PurchaseOrderStatusReceived));
                if (hasLinkedPurchaseOrders)
                {
                    return true;
                }
            }

            var planLines = _context.InventoryMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.OwnerUserID == ownerUserId &&
                    movement.ReferenceType == ProcurementReferenceType &&
                    movement.MovementType == ProcurementPlanMovementType &&
                    movement.Reason.Contains(ProcurementPlanReasonPrefix))
                .Select(movement => new { movement.ReferenceId, movement.Reason })
                .ToList();

            return planLines
                .GroupBy(planLine => planLine.ReferenceId)
                .Select(group => ParseProcurementPlanReason(group.First().Reason))
                .Any(metadata =>
                    metadata.BudgetId.HasValue &&
                    budgetIds.Contains(metadata.BudgetId.Value) &&
                    IsCommittedProcurementStatus(metadata.Status));
        }

        private static bool IsCommittedProcurementStatus(string? status)
        {
            var normalizedStatus = NormalizeProcurementStatus(status);
            return string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeProcurementStatus(string? status)
        {
            return status?.Trim() switch
            {
                var s when string.Equals(s, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusApproved,
                var s when string.Equals(s, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusPartiallyReceived,
                var s when string.Equals(s, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusReceived,
                _ => "Draft"
            };
        }

        private static (string Status, int? BudgetId) ParseProcurementPlanReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return ("Draft", null);
            }

            var status = "Draft";
            int? budgetId = null;
            var segments = reason.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                if (segment.StartsWith("STATUS=", StringComparison.OrdinalIgnoreCase))
                {
                    status = NormalizeProcurementStatus(segment["STATUS=".Length..]);
                    continue;
                }

                if (segment.StartsWith("BUDGET=", StringComparison.OrdinalIgnoreCase))
                {
                    var rawBudgetId = segment["BUDGET=".Length..];
                    if (int.TryParse(rawBudgetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBudgetId) &&
                        parsedBudgetId > 0)
                    {
                        budgetId = parsedBudgetId;
                    }
                }
            }

            return (status, budgetId);
        }

        private static IQueryable<T> ApplyOwnerFilter<T>(
            IQueryable<T> query,
            bool applyOwnerFilter,
            int ownerUserId) where T : class
        {
            if (!applyOwnerFilter)
            {
                return query;
            }

            if (typeof(T) == typeof(Order))
            {
                return (query as IQueryable<Order>)!
                    .Where(order => order.OwnerUserID == ownerUserId) as IQueryable<T> ?? query;
            }

            if (typeof(T) == typeof(Product))
            {
                return (query as IQueryable<Product>)!
                    .Where(product => product.OwnerUserID == ownerUserId) as IQueryable<T> ?? query;
            }

            if (typeof(T) == typeof(OrderDetail))
            {
                return (query as IQueryable<OrderDetail>)!
                    .Where(detail => detail.Order != null && detail.Order.OwnerUserID == ownerUserId) as IQueryable<T> ?? query;
            }

            if (typeof(T) == typeof(Customer))
            {
                return (query as IQueryable<Customer>)!
                    .Where(customer => customer.Orders.Any(order => order.OwnerUserID == ownerUserId)) as IQueryable<T> ?? query;
            }

            if (typeof(T) == typeof(InventoryMovement))
            {
                return (query as IQueryable<InventoryMovement>)!
                    .Where(movement => movement.OwnerUserID == ownerUserId) as IQueryable<T> ?? query;
            }

            return query;
        }

        private (bool ApplyOwnerFilter, int OwnerUserId) ResolveOwnerFilterContext()
        {
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            return (applyOwnerFilter, ownerUserId);
        }

        private int? ResolveWritableOwnerUserId()
        {
            if (_tenantContext.OwnerUserId.HasValue)
            {
                return _tenantContext.OwnerUserId.Value;
            }

            if (_tenantContext.IsSuperAdmin)
            {
                return null;
            }

            return _tenantContext.CurrentUserId;
        }

        private void AddBudgetHistoryLog(int ownerUserId, string action)
        {
            var actorUserId = _tenantContext.CurrentUserId ?? ownerUserId;
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId,
                OwnerUserID = ownerUserId,
                Action = action,
                Timestamp = DateTime.UtcNow
            });
        }

        private void AddBudgetEvent(
            int ownerUserId,
            int budgetId,
            string eventType,
            decimal amount,
            decimal? beforeAmount,
            decimal? afterAmount,
            string reason,
            string referenceType,
            string? referenceId,
            DateTime occurredAtUtc)
        {
            if (budgetId <= 0)
            {
                return;
            }

            var actorUserId = _tenantContext.CurrentUserId ?? ownerUserId;
            _context.BudgetEvents.Add(new BudgetEvent
            {
                OwnerUserID = ownerUserId,
                BudgetID = budgetId,
                EventType = string.IsNullOrWhiteSpace(eventType) ? BudgetEventTypeUpdate : eventType.Trim(),
                Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
                BeforeAmount = beforeAmount.HasValue
                    ? Math.Round(beforeAmount.Value, 2, MidpointRounding.AwayFromZero)
                    : null,
                AfterAmount = afterAmount.HasValue
                    ? Math.Round(afterAmount.Value, 2, MidpointRounding.AwayFromZero)
                    : null,
                Reason = string.IsNullOrWhiteSpace(reason) ? "No reason provided." : reason.Trim(),
                ReferenceType = string.IsNullOrWhiteSpace(referenceType)
                    ? BudgetEventReferenceTypeBudget
                    : referenceType.Trim(),
                ReferenceId = string.IsNullOrWhiteSpace(referenceId) ? string.Empty : referenceId.Trim(),
                PerformedByUserID = actorUserId,
                OccurredAtUtc = occurredAtUtc
            });
        }

        private static string BuildBudgetHistoryContextTag(int budgetId, DateTime monthStartLocal)
        {
            return $"{BudgetHistoryBudgetTagPrefix}{budgetId}] [Month:{monthStartLocal:yyyy-MM}]";
        }

        private static decimal CalculateEstimatedRevenueImpact(
            decimal currentPrice,
            decimal projectedPrice,
            decimal estimatedMonthlyUnits)
        {
            return Math.Round(
                (projectedPrice - currentPrice) * estimatedMonthlyUnits,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static (DateTime StartDate, DateTime EndDate) NormalizeLocalDateRange(
            DateTime startDate,
            DateTime endDate)
        {
            var normalizedStartDate = startDate.Date;
            var normalizedEndDate = endDate.Date;

            if (normalizedEndDate < normalizedStartDate)
            {
                (normalizedStartDate, normalizedEndDate) = (normalizedEndDate, normalizedStartDate);
            }

            return (normalizedStartDate, normalizedEndDate);
        }

        private static (DateTime RangeStartUtc, DateTime RangeEndUtcInclusive) NormalizeRange(
            DateTime startDateUtc,
            DateTime endDateUtc)
        {
            var (rangeStartLocalDate, rangeEndLocalDate) = NormalizeLocalDateRange(startDateUtc, endDateUtc);
            return (ConvertLocalDateStartToUtc(rangeStartLocalDate), ConvertLocalDateEndToUtc(rangeEndLocalDate));
        }

        private static string NormalizeSelectedDateRange(string? selectedDateRange)
        {
            if (string.IsNullOrWhiteSpace(selectedDateRange))
            {
                return "this_month";
            }

            return selectedDateRange.Trim().ToLowerInvariant() switch
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

        private static (DateTime StartDate, DateTime EndDate) ResolveComparisonLocalDateRange(
            string normalizedSelectedDateRange,
            DateTime rangeStartLocalDate,
            DateTime rangeEndLocalDate)
        {
            var (normalizedStartDate, normalizedEndDate) = NormalizeLocalDateRange(rangeStartLocalDate, rangeEndLocalDate);
            var spanDays = Math.Max(1, (normalizedEndDate - normalizedStartDate).Days + 1);

            return normalizedSelectedDateRange switch
            {
                "today" => (normalizedStartDate.AddDays(-1), normalizedStartDate.AddDays(-1)),
                "yesterday" => (normalizedStartDate.AddDays(-1), normalizedStartDate.AddDays(-1)),
                "this_month" => ResolvePreviousMonthToDate(normalizedEndDate, spanDays),
                "this_year" => ResolvePreviousYearToDate(normalizedEndDate, spanDays),
                _ => (normalizedStartDate.AddDays(-spanDays), normalizedStartDate.AddDays(-1))
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

        private static string BuildComparisonPeriodLabel(DateTime comparisonStartDate, DateTime comparisonEndDate)
        {
            var (normalizedStartDate, normalizedEndDate) = NormalizeLocalDateRange(comparisonStartDate, comparisonEndDate);

            if (normalizedStartDate == normalizedEndDate)
            {
                return $"Compared with {normalizedStartDate:MMM dd, yyyy}";
            }

            return $"Compared with {normalizedStartDate:MMM dd, yyyy} - {normalizedEndDate:MMM dd, yyyy}";
        }

        private static DateTime ConvertUtcToLocal(DateTime utcDateTime)
        {
            return BusinessTime.ConvertUtcToBusinessTime(utcDateTime);
        }

        private static DateTime ConvertLocalDateStartToUtc(DateTime localDate)
        {
            return BusinessTime.ConvertBusinessDateStartToUtc(localDate);
        }

        private static DateTime ConvertLocalDateEndToUtc(DateTime localDate)
        {
            return BusinessTime.ConvertBusinessDateEndToUtc(localDate);
        }

        private static bool MatchesPaymentFilter(Order order, string paymentFilter)
        {
            return paymentFilter switch
            {
                "paid" => IsPaidStatus(order.PaymentStatus),
                "pending" => IsPendingPaymentStatus(order.PaymentStatus),
                "refunded" => IsRefundedStatus(order.PaymentStatus),
                _ => true
            };
        }

        private static bool MatchesOrderStatusFilter(Order order, string orderStatusFilter)
        {
            return orderStatusFilter switch
            {
                "pending" => IsPendingStatus(order.OrderStatus),
                "processing" => string.Equals(order.OrderStatus, "Processing", StringComparison.OrdinalIgnoreCase),
                "completed" => IsCompletedStatus(order.OrderStatus),
                _ => true
            };
        }

        private static bool MatchesFinancialPaymentScope(Order order, string paymentScope)
        {
            return paymentScope switch
            {
                "all" => !IsCancelledStatus(order.OrderStatus),
                "pending_only" => !IsCancelledStatus(order.OrderStatus) && IsPendingPaymentStatus(order.PaymentStatus),
                "refunded_only" => IsRefundedStatus(order.PaymentStatus) && !IsCancelledStatus(order.OrderStatus),
                _ => RevenueRecognitionPolicy.IsRecognizedRevenueStatus(order.PaymentStatus, order.OrderStatus)
            };
        }

        private static string DetermineStockRisk(decimal daysOfStockCover)
        {
            if (daysOfStockCover <= 7m)
            {
                return "Critical";
            }

            if (daysOfStockCover <= 14m)
            {
                return "Warning";
            }

            return "Healthy";
        }

        private static bool IsPaidStatus(string? paymentStatus) =>
            string.Equals(paymentStatus, RevenueRecognitionPolicy.PaidPaymentStatus, StringComparison.OrdinalIgnoreCase);

        private static bool IsRefundedStatus(string? paymentStatus) =>
            string.Equals(paymentStatus, RevenueRecognitionPolicy.RefundedPaymentStatus, StringComparison.OrdinalIgnoreCase);

        private static bool IsPendingPaymentStatus(string? paymentStatus) =>
            string.IsNullOrWhiteSpace(paymentStatus) ||
            string.Equals(paymentStatus, "Pending", StringComparison.OrdinalIgnoreCase);

        private static bool IsPendingStatus(string? orderStatus) =>
            !string.IsNullOrWhiteSpace(orderStatus) &&
            PendingStatuses.Contains(orderStatus.Trim());

        private static bool IsCompletedStatus(string? orderStatus) =>
            !string.IsNullOrWhiteSpace(orderStatus) &&
            CompletedStatuses.Contains(orderStatus.Trim());

        private static bool IsCancelledStatus(string? orderStatus) =>
            string.Equals(orderStatus, RevenueRecognitionPolicy.CancelledOrderStatus, StringComparison.OrdinalIgnoreCase);

        private static decimal ResolveUnitCost(
            OrderDetail detail,
            IReadOnlyDictionary<(int OrderId, int ProductId), decimal> unitCostLookup)
        {
            if (unitCostLookup.TryGetValue((detail.OrderID, detail.ProductID), out var resolvedUnitCost) &&
                resolvedUnitCost > 0m)
            {
                return resolvedUnitCost;
            }

            return detail.Product?.CostPrice ?? 0m;
        }

        private static (List<string> Labels, List<decimal> Values) BuildRevenueTrend(
            IEnumerable<Order> filteredOrders,
            DateTime rangeStartLocalDate,
            DateTime rangeEndLocalDate)
        {
            // CALC-KPI: Use daily buckets for short ranges and monthly buckets for longer ranges.
            var (normalizedRangeStartLocalDate, normalizedRangeEndLocalDate) = NormalizeLocalDateRange(rangeStartLocalDate, rangeEndLocalDate);
            var days = (normalizedRangeEndLocalDate - normalizedRangeStartLocalDate).Days + 1;
            var recognizedRevenueOrders = filteredOrders
                .Where(RevenueRecognitionPolicy.IsRecognizedRevenueOrder)
                .ToList();
            if (days <= 31)
            {
                var revenueByDay = recognizedRevenueOrders
                    .GroupBy(order => ConvertUtcToLocal(order.OrderDate).Date)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(order => order.TotalAmount));

                var labels = new List<string>();
                var values = new List<decimal>();
                for (var cursor = normalizedRangeStartLocalDate; cursor <= normalizedRangeEndLocalDate; cursor = cursor.AddDays(1))
                {
                    labels.Add(cursor.ToString("MMM dd"));
                    values.Add(revenueByDay.TryGetValue(cursor, out var amount) ? amount : 0m);
                }

                return (labels, values);
            }

            var revenueByMonth = recognizedRevenueOrders
                .GroupBy(order =>
                {
                    var localOrderDate = ConvertUtcToLocal(order.OrderDate);
                    return new DateTime(localOrderDate.Year, localOrderDate.Month, 1);
                })
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(order => order.TotalAmount));

            var monthlyLabels = new List<string>();
            var monthlyValues = new List<decimal>();
            for (var cursor = new DateTime(normalizedRangeStartLocalDate.Year, normalizedRangeStartLocalDate.Month, 1);
                 cursor <= normalizedRangeEndLocalDate;
                 cursor = cursor.AddMonths(1))
            {
                monthlyLabels.Add(cursor.ToString("MMM yyyy"));
                monthlyValues.Add(revenueByMonth.TryGetValue(cursor, out var amount) ? amount : 0m);
            }

            return (monthlyLabels, monthlyValues);
        }

        private static (List<string> Labels, List<decimal> CogsValues, List<decimal> RevenueValues) BuildProfitTrend(
            IEnumerable<Order> filteredOrders,
            DateTime rangeStartLocalDate,
            DateTime rangeEndLocalDate,
            IReadOnlyDictionary<(int OrderId, int ProductId), decimal> unitCostLookup)
        {
            var (normalizedRangeStartLocalDate, normalizedRangeEndLocalDate) = NormalizeLocalDateRange(rangeStartLocalDate, rangeEndLocalDate);
            var days = (normalizedRangeEndLocalDate - normalizedRangeStartLocalDate).Days + 1;
            if (days <= 31)
            {
                var trendByDay = filteredOrders
                    .GroupBy(order => ConvertUtcToLocal(order.OrderDate).Date)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var cogs = group.Sum(order =>
                                order.OrderDetails
                                    .Where(detail => detail.Product != null)
                                    .Sum(detail => detail.Quantity * ResolveUnitCost(detail, unitCostLookup)));
                            var revenue = group.Sum(order => order.TotalAmount);
                            return (Cogs: cogs, Revenue: revenue);
                        });

                var labels = new List<string>();
                var cogsValues = new List<decimal>();
                var revenueValues = new List<decimal>();
                for (var cursor = normalizedRangeStartLocalDate; cursor <= normalizedRangeEndLocalDate; cursor = cursor.AddDays(1))
                {
                    labels.Add(cursor.ToString("MMM dd"));
                    if (trendByDay.TryGetValue(cursor, out var trend))
                    {
                        cogsValues.Add(trend.Cogs);
                        revenueValues.Add(trend.Revenue);
                    }
                    else
                    {
                        cogsValues.Add(0m);
                        revenueValues.Add(0m);
                    }
                }

                return (labels, cogsValues, revenueValues);
            }

            var trendByMonth = filteredOrders
                .GroupBy(order =>
                {
                    var localOrderDate = ConvertUtcToLocal(order.OrderDate);
                    return new DateTime(localOrderDate.Year, localOrderDate.Month, 1);
                })
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var cogs = group.Sum(order =>
                            order.OrderDetails
                                .Where(detail => detail.Product != null)
                                .Sum(detail => detail.Quantity * ResolveUnitCost(detail, unitCostLookup)));
                        var revenue = group.Sum(order => order.TotalAmount);
                        return (Cogs: cogs, Revenue: revenue);
                    });

            var monthlyLabels = new List<string>();
            var monthlyCogsValues = new List<decimal>();
            var monthlyRevenueValues = new List<decimal>();
            for (var cursor = new DateTime(normalizedRangeStartLocalDate.Year, normalizedRangeStartLocalDate.Month, 1);
                 cursor <= normalizedRangeEndLocalDate;
                 cursor = cursor.AddMonths(1))
            {
                monthlyLabels.Add(cursor.ToString("MMM yyyy"));
                if (trendByMonth.TryGetValue(cursor, out var trend))
                {
                    monthlyCogsValues.Add(trend.Cogs);
                    monthlyRevenueValues.Add(trend.Revenue);
                }
                else
                {
                    monthlyCogsValues.Add(0m);
                    monthlyRevenueValues.Add(0m);
                }
            }

            return (monthlyLabels, monthlyCogsValues, monthlyRevenueValues);
        }

        private static (List<string> Labels, List<decimal> ActualValues, List<decimal> BudgetValues) BuildBudgetTrackingTrend(
            IEnumerable<Order> filteredOrders,
            DateTime rangeStartLocalDate,
            DateTime rangeEndLocalDate,
            decimal budgetAmount)
        {
            var (normalizedRangeStartLocalDate, normalizedRangeEndLocalDate) = NormalizeLocalDateRange(rangeStartLocalDate, rangeEndLocalDate);
            var days = (normalizedRangeEndLocalDate - normalizedRangeStartLocalDate).Days + 1;

            if (days <= 62)
            {
                var revenueByDay = filteredOrders
                    .GroupBy(order => ConvertUtcToLocal(order.OrderDate).Date)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(order => order.TotalAmount));

                var labels = new List<string>();
                var actualValues = new List<decimal>();
                var budgetValues = new List<decimal>();

                var dailyBudgetTarget = days > 0
                    ? budgetAmount / days
                    : 0m;
                var cumulativeActual = 0m;
                var cumulativeBudgetTarget = 0m;

                for (var cursor = normalizedRangeStartLocalDate; cursor <= normalizedRangeEndLocalDate; cursor = cursor.AddDays(1))
                {
                    labels.Add(cursor.ToString("MMM dd"));
                    cumulativeActual += revenueByDay.TryGetValue(cursor, out var actualRevenue) ? actualRevenue : 0m;
                    cumulativeBudgetTarget += dailyBudgetTarget;

                    actualValues.Add(Math.Round(cumulativeActual, 2, MidpointRounding.AwayFromZero));
                    budgetValues.Add(Math.Round(cumulativeBudgetTarget, 2, MidpointRounding.AwayFromZero));
                }

                return (labels, actualValues, budgetValues);
            }

            var revenueByMonth = filteredOrders
                .GroupBy(order =>
                {
                    var localOrderDate = ConvertUtcToLocal(order.OrderDate);
                    return new DateTime(localOrderDate.Year, localOrderDate.Month, 1);
                })
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(order => order.TotalAmount));

            var monthlyLabels = new List<string>();
            var monthlyActualValues = new List<decimal>();
            var monthlyBudgetValues = new List<decimal>();
            var monthMarkers = new List<DateTime>();

            for (var cursor = new DateTime(normalizedRangeStartLocalDate.Year, normalizedRangeStartLocalDate.Month, 1);
                 cursor <= normalizedRangeEndLocalDate;
                 cursor = cursor.AddMonths(1))
            {
                monthMarkers.Add(cursor);
            }

            var monthlyBudgetTarget = monthMarkers.Count > 0
                ? budgetAmount / monthMarkers.Count
                : 0m;
            var cumulativeMonthlyActual = 0m;
            var cumulativeMonthlyBudget = 0m;

            foreach (var marker in monthMarkers)
            {
                monthlyLabels.Add(marker.ToString("MMM yyyy"));
                cumulativeMonthlyActual += revenueByMonth.TryGetValue(marker, out var monthlyRevenue) ? monthlyRevenue : 0m;
                cumulativeMonthlyBudget += monthlyBudgetTarget;

                monthlyActualValues.Add(Math.Round(cumulativeMonthlyActual, 2, MidpointRounding.AwayFromZero));
                monthlyBudgetValues.Add(Math.Round(cumulativeMonthlyBudget, 2, MidpointRounding.AwayFromZero));
            }

            return (monthlyLabels, monthlyActualValues, monthlyBudgetValues);
        }

        private static FinancialSummary BuildFinancialSummary(
            IEnumerable<Order> orders,
            IReadOnlyDictionary<(int OrderId, int ProductId), decimal> unitCostLookup)
        {
            var orderList = orders.ToList();
            // CALC-KPI: Financial summary derives revenue, COGS, gross profit, and margin from the same order set.
            var revenue = orderList.Sum(order => order.TotalAmount);
            var cogs = orderList
                .SelectMany(order => order.OrderDetails)
                .Where(detail => detail.Product != null)
                .Sum(detail => detail.Quantity * ResolveUnitCost(detail, unitCostLookup));
            var grossProfit = revenue - cogs;
            var margin = revenue > 0 ? Math.Round((grossProfit / revenue) * 100m, 2) : 0m;

            return new FinancialSummary(revenue, cogs, grossProfit, margin);
        }

        private static decimal CalculateChangePercentage(decimal current, decimal previous)
        {
            // CALC-HELPER: Shared percent-change math with divide-by-zero handling for BI KPIs.
            if (previous == 0m)
            {
                return current == 0m ? 0m : 100m;
            }

            return Math.Round(((current - previous) / previous) * 100m, 2);
        }

        private static decimal CalculateChangePercentageUsingAbsoluteBaseline(decimal current, decimal previous)
        {
            // CALC-HELPER: Financial trend math uses absolute baseline for intuitive gain/loss direction from negatives.
            if (previous == 0m)
            {
                return current == 0m ? 0m : 100m;
            }

            return Math.Round(((current - previous) / Math.Abs(previous)) * 100m, 2);
        }

        private static decimal CalculateChangePercentage(int current, int previous)
        {
            // CALC-HELPER: Int overload keeps KPI change calculations consistent with decimal overload.
            if (previous == 0)
            {
                return current == 0 ? 0 : 100m;
            }

            return Math.Round(((current - previous) / (decimal)previous) * 100m, 2);
        }

        private static int NormalizePageSize(int pageSize)
        {
            return AllowedPageSizes.Contains(pageSize) ? pageSize : 10;
        }

        private static int CalculateTotalPages(int totalCount, int pageSize)
        {
            return totalCount <= 0
                ? 1
                : (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize));
        }

        private static int NormalizePage(int page, int totalPages)
        {
            if (page <= 0)
            {
                return 1;
            }

            if (page > totalPages)
            {
                return totalPages;
            }

            return page;
        }

        private static string ResolveCustomerName(Customer? customer)
        {
            if (customer == null)
            {
                return "Guest";
            }

            if (!string.IsNullOrWhiteSpace(customer.FullName))
            {
                return customer.FullName;
            }

            return "Guest";
        }

        private static string NormalizePaymentFilter(string? paymentFilter) =>
            paymentFilter?.Trim().ToLowerInvariant() switch
            {
                "paid" => "paid",
                "pending" => "pending",
                "refunded" => "refunded",
                _ => "all"
            };

        private static string NormalizeOrderStatusFilter(string? orderStatusFilter) =>
            orderStatusFilter?.Trim().ToLowerInvariant() switch
            {
                "pending" => "pending",
                "processing" => "processing",
                "completed" => "completed",
                _ => "all"
            };

        private static string NormalizeFinancialPaymentScope(string? paymentScope) =>
            paymentScope?.Trim().ToLowerInvariant() switch
            {
                "all" => "all",
                "pending_only" => "pending_only",
                "refunded_only" => "refunded_only",
                _ => "paid_only"
            };

        private static string NormalizeAdjustmentFilter(string? adjustmentFilter) =>
            adjustmentFilter?.Trim().ToLowerInvariant() switch
            {
                "increase" => "Increase",
                "decrease" => "Decrease",
                "maintain" => "Maintain",
                _ => "all"
            };

        private readonly record struct BudgetSelectionContext(
            FinancialBudget? SelectedBudget,
            DateTime RangeStartDateLocal,
            DateTime RangeEndDateLocal,
            decimal BudgetAmount,
            string? SelectionMessage);

        private readonly record struct BudgetUsageSummary(
            decimal ReservedAmount,
            decimal SpentAmount);

        private readonly record struct BudgetRevenueAnalytics(
            List<Order> FilteredOrders,
            FinancialSummary Summary);

        private readonly record struct BudgetPlanningKpis(
            decimal IntegratedBudgetTarget,
            decimal BudgetGapToIntegratedTarget,
            decimal BudgetVariance,
            decimal BudgetUtilizationPercentage);

        private readonly record struct FinancialSummary(
            decimal Revenue,
            decimal CostOfGoodsSold,
            decimal GrossProfit,
            decimal Margin);
    }
}
