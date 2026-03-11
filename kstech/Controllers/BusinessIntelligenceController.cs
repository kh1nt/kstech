using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using kstech.Filters;
using kstech.Services;
using kstech.Utilities;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme")]
    [RequireOwnerScopeForSuperAdmin]
    public class BusinessIntelligenceController : Controller
    {
        private readonly IBusinessIntelligenceService _biService;
        private readonly IMarketPriceSyncService _marketPriceSyncService;
        private readonly ITenantContext _tenantContext;
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly IInventoryControlService _inventoryControlService;
        private readonly IReportPdfService _reportPdfService;
        private readonly IReportCloudArchiveService _reportCloudArchiveService;

        public BusinessIntelligenceController(
            IBusinessIntelligenceService biService,
            IMarketPriceSyncService marketPriceSyncService,
            ITenantContext tenantContext,
            kstech.Data.ApplicationDbContext context,
            IInventoryControlService inventoryControlService,
            IReportPdfService reportPdfService,
            IReportCloudArchiveService reportCloudArchiveService)
        {
            _biService = biService;
            _marketPriceSyncService = marketPriceSyncService;
            _tenantContext = tenantContext;
            _context = context;
            _inventoryControlService = inventoryControlService;
            _reportPdfService = reportPdfService;
            _reportCloudArchiveService = reportCloudArchiveService;
        }

        // Action of Index
        [Authorize(Roles = "SuperAdmin,Owner")]
        public IActionResult Index(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string adjustment = "all")
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedAdjustment = NormalizeAdjustmentFilter(adjustment);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var model = _biService.GetBusinessIntelligence(
                resolvedStartDate,
                resolvedEndDate,
                normalizedAdjustment);
            model.SelectedDateRange = normalizedRange;
            return View(model);
        }

        // Action of Sales
        [Authorize(Roles = "SuperAdmin,Owner,Sales Staff")]
        public IActionResult Sales(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string payment = "all",
            string orderStatus = "all",
            int page = 1,
            int pageSize = 10)
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedPayment = NormalizeSalesPaymentFilter(payment);
            var normalizedOrderStatus = NormalizeSalesOrderStatusFilter(orderStatus);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var model = _biService.GetSalesAnalytics(
                resolvedStartDate,
                resolvedEndDate,
                normalizedRange,
                normalizedPayment,
                normalizedOrderStatus,
                page,
                pageSize);
            model.SelectedDateRange = normalizedRange;
            return View(model);
        }

        [Authorize(Roles = "SuperAdmin,Owner,Sales Staff")]
        [HttpGet]
        public IActionResult ExportSalesReportPdf(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string payment = "all",
            string orderStatus = "all")
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedPayment = NormalizeSalesPaymentFilter(payment);
            var normalizedOrderStatus = NormalizeSalesOrderStatusFilter(orderStatus);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var model = _biService.GetSalesAnalytics(
                resolvedStartDate,
                resolvedEndDate,
                normalizedRange,
                normalizedPayment,
                normalizedOrderStatus,
                recentSalesPage: 1,
                recentSalesPageSize: 50);
            model.SelectedDateRange = normalizedRange;

            var reportBytes = _reportPdfService.BuildSalesReport(model);
            var fileName = $"sales-report-{model.FilterStartDate:yyyyMMdd}-{model.FilterEndDate:yyyyMMdd}.pdf";
            _reportCloudArchiveService.TryUploadReport(
                reportType: "sales",
                fileName: fileName,
                reportBytes: reportBytes,
                ownerUserId: _tenantContext.OwnerUserId,
                periodStartLocal: model.FilterStartDate,
                periodEndLocal: model.FilterEndDate);

            return File(reportBytes, "application/pdf", fileName);
        }

        // Action of CancelOrder
        [Authorize(Roles = "SuperAdmin,Owner,Sales Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["SalesMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Sales));
            }

            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                TempData["SalesMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Sales));
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderID == orderId && o.OwnerUserID == ownerUserId.Value);

            if (order == null ||
                (!string.Equals(order.OrderStatus, "Pending", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(order.OrderStatus, "Processing", StringComparison.OrdinalIgnoreCase)))
            {
                TempData["SalesMessage"] = "Order not found or cannot be cancelled.";
                return RedirectToAction(nameof(Sales));
            }

            order.OrderStatus = "Cancelled";
            var originalPaymentStatus = order.PaymentStatus ?? string.Empty;
            order.PaymentStatus = string.Equals(
                originalPaymentStatus,
                RevenueRecognitionPolicy.PaidPaymentStatus,
                StringComparison.OrdinalIgnoreCase)
                ? RevenueRecognitionPolicy.RefundedPaymentStatus
                : originalPaymentStatus;

            var actorUserId = _tenantContext.CurrentUserId ?? ownerUserId.Value;
            var productIds = order.OrderDetails
                .Select(detail => detail.ProductID)
                .Distinct()
                .ToList();
            var productsById = await _context.Products
                .Where(product =>
                    product.OwnerUserID == ownerUserId.Value &&
                    productIds.Contains(product.ProductID))
                .ToDictionaryAsync(product => product.ProductID);

            foreach (var detail in order.OrderDetails)
            {
                if (productsById.TryGetValue(detail.ProductID, out var product))
                {
                    _inventoryControlService.ApplyStockIn(
                        product,
                        detail.Quantity,
                        detail.UnitPriceAtSale,
                        "Admin",
                        "Order Cancelled by Admin",
                        "Order",
                        order.OrderID.ToString(),
                        actorUserId
                    );
                }
            }

            await _context.SaveChangesAsync();
            TempData["SalesMessage"] = $"Order #{orderId} has been successfully cancelled.";
            return RedirectToAction(nameof(Sales));
        }

        // Action of RefundOrder
        [Authorize(Roles = "SuperAdmin,Owner,Sales Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefundOrder(int orderId)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["SalesMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Sales));
            }

            var ownerUserId = ResolveWritableOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                TempData["SalesMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Sales));
            }

            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderID == orderId && o.OwnerUserID == ownerUserId.Value);

            if (order == null ||
                !string.Equals(order.PaymentStatus, RevenueRecognitionPolicy.PaidPaymentStatus, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.OrderStatus, RevenueRecognitionPolicy.CancelledOrderStatus, StringComparison.OrdinalIgnoreCase))
            {
                TempData["SalesMessage"] = "Order not found or not eligible for refund.";
                return RedirectToAction(nameof(Sales));
            }

            order.PaymentStatus = RevenueRecognitionPolicy.RefundedPaymentStatus;

            await _context.SaveChangesAsync();
            TempData["SalesMessage"] = $"Order #{orderId} payment has been marked as refunded.";
            return RedirectToAction(nameof(Sales));
        }

        // Action of Financial
        [Authorize(Roles = "SuperAdmin,Owner")]
        public IActionResult Financial(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string paymentScope = "paid_only",
            int page = 1,
            int pageSize = 10)
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedPaymentScope = NormalizeFinancialPaymentScope(paymentScope);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var model = _biService.GetFinancialPerformance(
                resolvedStartDate,
                resolvedEndDate,
                normalizedRange,
                normalizedPaymentScope,
                page,
                pageSize);
            model.SelectedDateRange = normalizedRange;
            return View(model);
        }

        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpGet]
        public IActionResult ExportFinancialReportPdf(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            string paymentScope = "paid_only")
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedPaymentScope = NormalizeFinancialPaymentScope(paymentScope);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var model = _biService.GetFinancialPerformance(
                resolvedStartDate,
                resolvedEndDate,
                normalizedRange,
                normalizedPaymentScope,
                recentTransactionsPage: 1,
                recentTransactionsPageSize: 50);
            model.SelectedDateRange = normalizedRange;

            var reportBytes = _reportPdfService.BuildFinancialReport(model);
            var fileName = $"financial-report-{model.FilterStartDate:yyyyMMdd}-{model.FilterEndDate:yyyyMMdd}.pdf";
            _reportCloudArchiveService.TryUploadReport(
                reportType: "financial",
                fileName: fileName,
                reportBytes: reportBytes,
                ownerUserId: _tenantContext.OwnerUserId,
                periodStartLocal: model.FilterStartDate,
                periodEndLocal: model.FilterEndDate);

            return File(reportBytes, "application/pdf", fileName);
        }

        // Action of Budget
        [Authorize(Roles = "SuperAdmin,Owner")]
        public IActionResult Budget(
            int? budgetId = null,
            bool showArchivedBudgets = false,
            string? budgetMonth = null)
        {
            DateTime? parsedBudgetMonthLocal = null;
            if (!string.IsNullOrWhiteSpace(budgetMonth) &&
                DateTime.TryParseExact(
                    budgetMonth.Trim(),
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedBudgetMonth))
            {
                parsedBudgetMonthLocal = parsedBudgetMonth;
            }

            var model = _biService.GetBudgetPlanning(
                budgetId,
                showArchivedBudgets,
                parsedBudgetMonthLocal);
            return View(model);
        }

        // Action of SaveFinancialBudget
        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveFinancialBudget(
            int? selectedBudgetId,
            decimal budgetAmount,
            string budgetMonth,
            string? changeReason,
            bool showArchivedBudgets = false)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["FinancialBudgetMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Budget), new { showArchivedBudgets });
            }

            if (string.IsNullOrWhiteSpace(budgetMonth) ||
                !DateTime.TryParseExact(
                    budgetMonth.Trim(),
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedBudgetMonth))
            {
                TempData["FinancialBudgetMessage"] = "Select a valid budget month.";
                return RedirectToAction(nameof(Budget), new { budgetId = selectedBudgetId, showArchivedBudgets });
            }

            var saveResult = _biService.SaveFinancialBudget(
                selectedBudgetId,
                parsedBudgetMonth,
                budgetAmount,
                changeReason);
            TempData["FinancialBudgetMessage"] = saveResult.Message;

            return saveResult.Succeeded && saveResult.BudgetId.HasValue
                ? RedirectToAction(nameof(Budget), new { budgetId = saveResult.BudgetId.Value, showArchivedBudgets })
                : RedirectToAction(nameof(Budget), new
                {
                    budgetId = selectedBudgetId,
                    showArchivedBudgets,
                    budgetMonth = parsedBudgetMonth.ToString("yyyy-MM")
                });
        }

        // Action of ArchiveFinancialBudget
        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ArchiveFinancialBudget(
            int budgetId,
            bool showArchivedBudgets = false,
            string? budgetMonth = null)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["FinancialBudgetMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Budget), new { showArchivedBudgets, budgetMonth });
            }

            var archived = _biService.TryArchiveFinancialBudget(budgetId, out var message);
            TempData["FinancialBudgetMessage"] = message;
            return archived
                ? RedirectToAction(nameof(Budget), new { showArchivedBudgets, budgetMonth })
                : RedirectToAction(nameof(Budget), new { budgetId, showArchivedBudgets, budgetMonth });
        }

        // Action of RestoreFinancialBudget
        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RestoreFinancialBudget(
            int budgetId,
            bool showArchivedBudgets = true,
            string? budgetMonth = null)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["FinancialBudgetMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Budget), new { showArchivedBudgets, budgetMonth });
            }

            var restored = _biService.TryRestoreFinancialBudget(budgetId, out var message);
            TempData["FinancialBudgetMessage"] = message;
            return restored
                ? RedirectToAction(nameof(Budget), new
                {
                    budgetId,
                    showArchivedBudgets = false,
                    budgetMonth
                })
                : RedirectToAction(nameof(Budget), new { budgetId, showArchivedBudgets, budgetMonth });
        }

        // Action of RefreshMarketData
        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshMarketData()
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["MarketSyncMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _marketPriceSyncService.SyncProductPricesAsync();
            TempData["MarketSyncMessage"] =
                $"Market sync complete. Checked: {result.TotalChecked}, Updated: {result.Updated}, Failed: {result.Failed}.";
            return RedirectToAction(nameof(Index));
        }

        // Action of UpdateProductPrice
        [Authorize(Roles = "SuperAdmin,Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProductPrice(
            int productId,
            decimal newPrice,
            DateTime startDate,
            DateTime endDate,
            string range = "this_month",
            string adjustment = "all")
        {
            var normalizedRange = NormalizeDateRange(range);
            var normalizedAdjustment = NormalizeAdjustmentFilter(adjustment);
            var normalizedStartDate = startDate.Date;
            var normalizedEndDate = endDate.Date;
            if (normalizedEndDate < normalizedStartDate)
            {
                (normalizedStartDate, normalizedEndDate) = (normalizedEndDate, normalizedStartDate);
            }

            if (IsSuperAdminScopedReadOnly())
            {
                TempData["PriceUpdateError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index), new
                {
                    range = normalizedRange,
                    startDate = normalizedStartDate,
                    endDate = normalizedEndDate,
                    adjustment = normalizedAdjustment
                });
            }

            if (newPrice <= 0m)
            {
                TempData["PriceUpdateError"] = "Enter a valid price greater than zero.";
                return RedirectToAction(nameof(Index), new
                {
                    range = normalizedRange,
                    startDate = normalizedStartDate,
                    endDate = normalizedEndDate,
                    adjustment = normalizedAdjustment
                });
            }

            var updated = _biService.TryUpdateProductPrice(productId, newPrice);
            TempData[updated ? "PriceUpdateMessage" : "PriceUpdateError"] = updated
                ? "Product price updated."
                : "Unable to update product price.";

            return RedirectToAction(nameof(Index), new
            {
                range = normalizedRange,
                startDate = normalizedStartDate,
                endDate = normalizedEndDate,
                adjustment = normalizedAdjustment
            });
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

        private static string NormalizeAdjustmentFilter(string? adjustment)
        {
            return adjustment?.Trim().ToLowerInvariant() switch
            {
                "increase" => "increase",
                "decrease" => "decrease",
                "maintain" => "maintain",
                _ => "all"
            };
        }

        private static string NormalizeSalesPaymentFilter(string? payment)
        {
            return payment?.Trim().ToLowerInvariant() switch
            {
                "paid" => "paid",
                "pending" => "pending",
                "refunded" => "refunded",
                _ => "all"
            };
        }

        private static string NormalizeSalesOrderStatusFilter(string? orderStatus)
        {
            return orderStatus?.Trim().ToLowerInvariant() switch
            {
                "pending" => "pending",
                "processing" => "processing",
                "completed" => "completed",
                _ => "all"
            };
        }

        private static string NormalizeFinancialPaymentScope(string? paymentScope)
        {
            return paymentScope?.Trim().ToLowerInvariant() switch
            {
                "all" => "all",
                "pending_only" => "pending_only",
                "refunded_only" => "refunded_only",
                _ => "paid_only"
            };
        }

        private static (DateTime StartDate, DateTime EndDate) ResolveDateRange(
            string normalizedRange,
            DateTime? startDate,
            DateTime? endDate)
        {
            var today = BusinessTime.Today;

            return normalizedRange switch
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
        }

        private bool IsSuperAdminScopedReadOnly()
        {
            return _tenantContext.IsSuperAdmin &&
                   (!_tenantContext.HasOwnerScope || !_tenantContext.CanEditOwnerWorkspace);
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
    }
}
