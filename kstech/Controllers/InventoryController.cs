using kstech.Models;
using kstech.Models.ViewModels;
using kstech.Data;
using kstech.Models.Entities;
using kstech.Filters;
using kstech.Services;
using kstech.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin,Owner,Inventory Manager")]
    [RequireOwnerScopeForSuperAdmin]
    public class InventoryController : Controller
    {
        private static readonly int[] AllowedPageSizes = { 10, 20, 50 };
        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventoryService;
        private readonly IMarketPriceSyncService _marketPriceSyncService;
        private readonly IEbayBrowseService _ebayBrowseService;
        private readonly ITenantContext _tenantContext;
        private readonly IReportPdfService _reportPdfService;
        private readonly IReportCloudArchiveService _reportCloudArchiveService;

        public InventoryController(
            ApplicationDbContext context,
            IInventoryService inventoryService,
            IMarketPriceSyncService marketPriceSyncService,
            IEbayBrowseService ebayBrowseService,
            ITenantContext tenantContext,
            IReportPdfService reportPdfService,
            IReportCloudArchiveService reportCloudArchiveService)
        {
            _context = context;
            _inventoryService = inventoryService;
            _marketPriceSyncService = marketPriceSyncService;
            _ebayBrowseService = ebayBrowseService;
            _tenantContext = tenantContext;
            _reportPdfService = reportPdfService;
            _reportCloudArchiveService = reportCloudArchiveService;
        }

        // Action of Index
        public IActionResult Index(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month")
        {
            var normalizedRange = NormalizeDateRange(range);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var viewModel = _inventoryService.GetDashboardStats(resolvedStartDate, resolvedEndDate);
            viewModel.SelectedDateRange = normalizedRange;
            return View(viewModel);
        }

        [HttpGet]
        public IActionResult ExportInventoryReportPdf(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month")
        {
            var normalizedRange = NormalizeDateRange(range);
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var dashboardModel = _inventoryService.GetDashboardStats(resolvedStartDate, resolvedEndDate);
            dashboardModel.SelectedDateRange = normalizedRange;

            var inventoryModel = _inventoryService.GetInventoryStats(
                showArchived: false,
                searchTerm: string.Empty,
                category: "All",
                stockStatus: "All",
                sortBy: "name_asc",
                page: 1,
                pageSize: 50);

            var reportBytes = _reportPdfService.BuildInventoryReport(dashboardModel, inventoryModel);
            var fileName = $"inventory-report-{dashboardModel.FilterStartDate:yyyyMMdd}-{dashboardModel.FilterEndDate:yyyyMMdd}.pdf";
            _reportCloudArchiveService.TryUploadReport(
                reportType: "inventory",
                fileName: fileName,
                reportBytes: reportBytes,
                ownerUserId: _tenantContext.OwnerUserId,
                periodStartLocal: dashboardModel.FilterStartDate,
                periodEndLocal: dashboardModel.FilterEndDate);

            return File(reportBytes, "application/pdf", fileName);
        }

        // Action of Products
        public IActionResult Products(
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            var viewModel = _inventoryService.GetInventoryStats(
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize);
            return View(viewModel);
        }

        // Action of History
        [HttpGet]
        public IActionResult History(
            string search = "",
            string type = "all",
            DateTime? startDate = null,
            DateTime? endDate = null,
            string range = "this_month",
            int page = 1,
            int pageSize = 10)
        {
            var normalizedSearch = (search ?? string.Empty).Trim();
            var normalizedType = NormalizeHistoryType(type);
            var normalizedRange = NormalizeDateRange(range);
            const int normalizedPageSize = 10;
            var (resolvedStartDate, resolvedEndDate) = ResolveDateRange(normalizedRange, startDate, endDate);

            var today = BusinessTime.Today;
            var rangeStartLocalDate = resolvedStartDate?.Date ?? startDate?.Date ?? today;
            var rangeEndLocalDate = resolvedEndDate?.Date ?? endDate?.Date ?? rangeStartLocalDate;
            if (rangeEndLocalDate < rangeStartLocalDate)
            {
                rangeEndLocalDate = rangeStartLocalDate;
            }

            var rangeStartUtc = BusinessTime.ConvertBusinessDateStartToUtc(rangeStartLocalDate);
            var rangeEndUtc = BusinessTime.ConvertBusinessDateEndToUtc(rangeEndLocalDate);

            var typeOptions = BuildHistoryTypeOptions();
            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return View(new InventoryHistoryIndexViewModel
                {
                    Search = normalizedSearch,
                    Type = normalizedType,
                    Days = 30,
                    SelectedDateRange = normalizedRange,
                    FilterStartDate = rangeStartLocalDate,
                    FilterEndDate = rangeEndLocalDate,
                    Page = 1,
                    PageSize = normalizedPageSize,
                    TotalMatched = 0,
                    TypeOptions = typeOptions,
                    Items = new List<InventoryHistoryItemViewModel>()
                });
            }

            var movementQuery = _context.InventoryMovements
                .AsNoTracking()
                .Include(movement => movement.Product)
                .Include(movement => movement.PerformedByUser)
                .Where(movement =>
                    movement.OwnerUserID == ownerUserId.Value &&
                    movement.OccurredAtUtc >= rangeStartUtc &&
                    movement.OccurredAtUtc <= rangeEndUtc);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                movementQuery = movementQuery.Where(movement =>
                    movement.MovementType.Contains(normalizedSearch) ||
                    movement.Reason.Contains(normalizedSearch) ||
                    movement.ReferenceType.Contains(normalizedSearch) ||
                    movement.ReferenceId.Contains(normalizedSearch) ||
                    movement.PartnerName.Contains(normalizedSearch) ||
                    (movement.PerformedByUser != null && movement.PerformedByUser.Role.Contains(normalizedSearch)) ||
                    (movement.Product != null &&
                        (movement.Product.ProductName.Contains(normalizedSearch) ||
                         movement.Product.Sku.Contains(normalizedSearch))));
            }

            var movementRows = movementQuery
                .ToList()
                .Select(MapInventoryMovementToHistoryItem)
                .ToList();

            var auditLogQuery = _context.SystemLogs
                .AsNoTracking()
                .Include(log => log.User)
                .Where(log =>
                    log.OwnerUserID == ownerUserId.Value &&
                    log.Timestamp >= rangeStartUtc &&
                    log.Timestamp <= rangeEndUtc &&
                    EF.Functions.Like(log.Action, "Inventory:%"));

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                auditLogQuery = auditLogQuery.Where(log =>
                    log.Action.Contains(normalizedSearch) ||
                    (log.User != null &&
                        (log.User.FullName.Contains(normalizedSearch) ||
                         log.User.Role.Contains(normalizedSearch))));
            }

            var auditRows = auditLogQuery
                .ToList()
                .Select(MapInventoryAuditLogToHistoryItem)
                .ToList();

            var matchedRows = ApplyHistoryTypeFilter(
                    movementRows.Concat(auditRows).ToList(),
                    normalizedType)
                .OrderByDescending(item => item.TimestampUtc)
                .ToList();

            var totalMatched = matchedRows.Count;
            var totalPages = CalculateTotalPages(totalMatched, normalizedPageSize);
            var normalizedPage = NormalizePage(page, totalPages);

            var pagedRows = matchedRows
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return View(new InventoryHistoryIndexViewModel
            {
                Search = normalizedSearch,
                Type = normalizedType,
                Days = 30,
                SelectedDateRange = normalizedRange,
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalMatched = totalMatched,
                TypeOptions = typeOptions,
                Items = pagedRows
            });
        }

        // Action of RefreshMarketPrices
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshMarketPrices(
            int maxProducts = 50,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["MarketSyncMessage"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var result = await _marketPriceSyncService.SyncProductPricesAsync(maxProducts);
            TempData["MarketSyncMessage"] =
                $"eBay sync complete. Checked: {result.TotalChecked}, Updated: {result.Updated}, Failed: {result.Failed}.";
            TryAddInventoryAuditLog(
                $"Market prices refreshed. Checked: {result.TotalChecked}, Updated: {result.Updated}, Failed: {result.Failed}.");

            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        // Action of Procurement
        public IActionResult Procurement(
            int page = 1,
            int pageSize = 10,
            string sortBy = "date_desc",
            string budgetFilter = "all",
            string statusFilter = "all")
        {
            var normalizedPageSize = NormalizePageSize(pageSize);
            var normalizedSortBy = NormalizeProcurementSortBy(sortBy);
            var normalizedBudgetFilter = NormalizeProcurementBudgetFilter(budgetFilter);
            var normalizedStatusFilter = NormalizeProcurementStatusFilter(statusFilter);

            var allProcurements = _inventoryService.GetProcurements();
            var filteredProcurements = FilterProcurements(
                allProcurements,
                normalizedBudgetFilter,
                normalizedStatusFilter);
            var sortedProcurements = SortProcurements(filteredProcurements, normalizedSortBy).ToList();

            var totalMatched = sortedProcurements.Count;
            var totalPages = CalculateTotalPages(totalMatched, normalizedPageSize);
            var normalizedPage = NormalizePage(page, totalPages);

            var pagedProcurements = sortedProcurements
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            PopulateProcurementModalOptions();
            return View(new ProcurementListViewModel
            {
                Procurements = pagedProcurements,
                ReorderSuggestions = _inventoryService.GetReorderSuggestions(),
                SortBy = normalizedSortBy,
                BudgetFilter = normalizedBudgetFilter,
                StatusFilter = normalizedStatusFilter,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalMatched = totalMatched
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateAutoReorderDraftPurchaseOrder(
            int maxItems = 5,
            string supplierName = "",
            int page = 1,
            int pageSize = 10,
            string sortBy = "date_desc",
            string budgetFilter = "all",
            string statusFilter = "all")
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProcurementError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Procurement), new
                {
                    page,
                    pageSize,
                    sortBy,
                    budgetFilter,
                    statusFilter
                });
            }

            var result = _inventoryService.CreateAutoReorderDraftPurchaseOrder(maxItems, supplierName);
            TempData[result.Succeeded ? "ProcurementMessage" : "ProcurementError"] = result.Message;
            if (result.Succeeded)
            {
                TryAddInventoryAuditLog(
                    $"Procurement auto-reorder draft created. Supplier: {(string.IsNullOrWhiteSpace(supplierName) ? "Unspecified" : supplierName.Trim())}, Max items: {maxItems}.");
            }
            return RedirectToAction(nameof(Procurement), new
            {
                page,
                pageSize,
                sortBy,
                budgetFilter,
                statusFilter
            });
        }

        // Action of CreateProcurement
        [HttpGet]
        public IActionResult CreateProcurement()
        {
            PopulateProcurementModalOptions();
            return View();
        }

        // Action of CreateProcurement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateProcurement(ProcurementViewModel model)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProcurementError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Procurement));
            }

            if (string.IsNullOrWhiteSpace(model.SupplierName))
            {
                TempData["ProcurementError"] = "Supplier name is required.";
                return RedirectToAction(nameof(Procurement));
            }

            var validItems = (model.Items ?? new List<ProcurementItemViewModel>())
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.ProductId) &&
                    (item.QuantityOrdered > 0 || item.QuantityReceived > 0) &&
                    item.CostPerItem >= 0m)
                .ToList();

            if (!validItems.Any())
            {
                TempData["ProcurementError"] = "Add at least one valid procurement item.";
                return RedirectToAction(nameof(Procurement));
            }

            model.SupplierName = model.SupplierName.Trim();
            model.Items = validItems;

            var result = _inventoryService.AddProcurement(model);
            TempData[result.Succeeded ? "ProcurementMessage" : "ProcurementError"] = result.Message;
            if (result.Succeeded)
            {
                TryAddInventoryAuditLog(
                    $"Procurement created. Supplier: {model.SupplierName}, Items: {model.Items.Count}, BudgetId: {(model.BudgetId.HasValue ? model.BudgetId.Value.ToString() : "none")}.");
            }
            return RedirectToAction(nameof(Procurement));
        }

        // Action of ApproveProcurement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveProcurement(int purchaseOrderId)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProcurementError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Procurement));
            }

            if (!CanManageProcurementWorkflowActions())
            {
                TempData["ProcurementError"] = "Only owners can approve, receive, or delete purchase orders. Inventory Managers can create draft POs.";
                return RedirectToAction(nameof(Procurement));
            }

            var result = _inventoryService.ApproveProcurement(purchaseOrderId);
            TempData[result.Succeeded ? "ProcurementMessage" : "ProcurementError"] = result.Message;
            if (result.Succeeded)
            {
                TryAddInventoryAuditLog($"Procurement approved. PO ID: {purchaseOrderId}.");
            }
            return RedirectToAction(nameof(Procurement));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteDraftProcurement(int purchaseOrderId)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProcurementError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Procurement));
            }

            if (!CanManageProcurementWorkflowActions())
            {
                TempData["ProcurementError"] = "Only owners can approve, receive, or delete purchase orders. Inventory Managers can create draft POs.";
                return RedirectToAction(nameof(Procurement));
            }

            var result = _inventoryService.DeleteDraftProcurement(purchaseOrderId);
            TempData[result.Succeeded ? "ProcurementMessage" : "ProcurementError"] = result.Message;
            if (result.Succeeded)
            {
                TryAddInventoryAuditLog($"Procurement draft deleted. PO ID: {purchaseOrderId}.");
            }
            return RedirectToAction(nameof(Procurement));
        }

        // Action of ReceiveProcurement
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReceiveProcurement(int purchaseOrderId, List<ProcurementReceiveLineInput>? receiveLines = null)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProcurementError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Procurement));
            }

            if (!CanManageProcurementWorkflowActions())
            {
                TempData["ProcurementError"] = "Only owners can approve, receive, or delete purchase orders. Inventory Managers can create draft POs.";
                return RedirectToAction(nameof(Procurement));
            }

            var result = _inventoryService.ReceiveProcurement(purchaseOrderId, receiveLines);
            TempData[result.Succeeded ? "ProcurementMessage" : "ProcurementError"] = result.Message;
            if (result.Succeeded)
            {
                var lineCount = receiveLines?.Count ?? 0;
                TryAddInventoryAuditLog($"Procurement received. PO ID: {purchaseOrderId}, Receive lines: {lineCount}.");
            }
            return RedirectToAction(nameof(Procurement));
        }

        // Action of AddProduct
        [HttpGet]
        public IActionResult AddProduct()
        {
            var model = new ProductViewModel();
            PopulateProductSelectionOptions(model);
            return View(model);
        }

        // Action of AddProduct
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProduct(ProductViewModel model)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products));
            }

            NormalizeCustomCategoryAndBrand(model);

            if (IsReservedCategoryName(model.Category))
            {
                TempData["ProductError"] = "Category names 'All' and '__OTHER__' are reserved. Choose a different category.";
                return RedirectToAction(nameof(Products));
            }

            if (!TryValidateProductUpsertForm(model, isEdit: false, out var addValidationMessage))
            {
                TempData["ProductError"] = addValidationMessage;
                return RedirectToAction(nameof(Products));
            }

            // Immediately fetch eBay price on creation
            var queryTerm = string.IsNullOrWhiteSpace(model.Name) ? model.Sku : model.Name;
            var quote = await _ebayBrowseService.GetMarketPriceAsync(queryTerm);
            if (quote != null && quote.Price > 0)
            {
                model.EbayLivePrice = quote.Price;
            }

            _inventoryService.AddProduct(model);
            TempData["ProductMessage"] = "Product added.";
            TryAddInventoryAuditLog(
                $"Product added. {BuildProductAuditDescriptor(model.Name, model.Sku)}, Opening stock: {model.StockQuantity}, Unit cost: {model.UnitCost:0.##}.");
            return RedirectToAction(nameof(Products));
        }

        // Action of GetProductForEdit
        [HttpGet]
        public IActionResult GetProductForEdit(string id)
        {
            var product = _inventoryService.GetProduct(id);
            if (product == null)
            {
                return NotFound();
            }

            return Json(product);
        }

        // Action of EditProduct
        [HttpGet]
        public IActionResult EditProduct(string id)
        {
            var product = _inventoryService.GetProduct(id);
            if (product == null)
            {
                return RedirectToAction(nameof(Products));
            }

            return View(product);
        }

        // Action of EditProduct
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProduct(
            ProductViewModel model,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            NormalizeCustomCategoryAndBrand(model);

            if (IsReservedCategoryName(model.Category))
            {
                TempData["ProductError"] = "Category names 'All' and '__OTHER__' are reserved. Choose a different category.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            if (!TryValidateProductUpsertForm(model, isEdit: true, out var editValidationMessage))
            {
                TempData["ProductError"] = editValidationMessage;
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            // Optimistically update the eBay price on save
            var queryTerm = string.IsNullOrWhiteSpace(model.Name) ? model.Sku : model.Name;
            var quote = await _ebayBrowseService.GetMarketPriceAsync(queryTerm);
            if (quote != null && quote.Price > 0)
            {
                model.EbayLivePrice = quote.Price;
            }

            _inventoryService.UpdateProduct(model);
            TempData["ProductMessage"] = "Product updated.";
            TryAddInventoryAuditLog(
                $"Product updated. ID: {model.Id}, {BuildProductAuditDescriptor(model.Name, model.Sku)}, Stock: {model.StockQuantity}, Unit cost: {model.UnitCost:0.##}, Retail: {model.RetailPrice:0.##}.");
            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProductCondition(
            string id,
            int damagedQuantity,
            string conditionStatus = "Good",
            string? conditionNotes = null,
            DateTime? lastConditionCheckUtc = null,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            if (string.IsNullOrWhiteSpace(id) || !int.TryParse(id, out _))
            {
                TempData["ProductError"] = "Invalid product ID.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            if (damagedQuantity < 0)
            {
                TempData["ProductError"] = "Damaged quantity cannot be negative.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var normalizedConditionStatus = conditionStatus ?? "Good";
            var allowedConditionStatuses = new[] { "Good", "Damaged", "Defective", "For Inspection", "Expired" };
            if (!allowedConditionStatuses.Contains(normalizedConditionStatus, StringComparer.OrdinalIgnoreCase))
            {
                TempData["ProductError"] = "Condition status is invalid.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var updated = _inventoryService.UpdateProductCondition(
                id,
                damagedQuantity,
                normalizedConditionStatus,
                conditionNotes,
                lastConditionCheckUtc);

            TempData[updated ? "ProductMessage" : "ProductError"] = updated
                ? "Product quality details updated."
                : "Unable to update product quality details.";

            if (updated)
            {
                TryAddInventoryAuditLog(
                    $"Product quality updated. Product ID: {id}, Damaged: {damagedQuantity}, Condition: {normalizedConditionStatus}.");
            }

            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult QueueSlowMoverPromotion(
            int productId,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var result = _inventoryService.QueueSlowMoverPromotion(productId);
            TempData[result.Succeeded ? "ProductMessage" : "ProductError"] = result.Message;
            if (result.Succeeded)
            {
                TryAddInventoryAuditLog($"Slow-mover promotion queued for product ID {productId}.");
            }
            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        // Action of ArchiveProduct
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ArchiveProduct(
            string id,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = false,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var product = _inventoryService.GetProduct(id, includeArchived: true);
            _inventoryService.ArchiveProduct(id);
            if (product != null)
            {
                TryAddInventoryAuditLog($"Product archived. {BuildProductAuditDescriptor(product.Name, product.Sku)}.");
            }
            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        // Action of UnarchiveProduct
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnarchiveProduct(
            string id,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            bool showArchived = true,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["ProductError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Products), new
                {
                    showArchived,
                    searchTerm,
                    category,
                    stockStatus,
                    sortBy,
                    page,
                    pageSize
                });
            }

            var product = _inventoryService.GetProduct(id, includeArchived: true);
            _inventoryService.UnarchiveProduct(id);
            if (product != null)
            {
                TryAddInventoryAuditLog($"Product unarchived. {BuildProductAuditDescriptor(product.Name, product.Sku)}.");
            }
            return RedirectToAction(nameof(Products), new
            {
                showArchived,
                searchTerm,
                category,
                stockStatus,
                sortBy,
                page,
                pageSize
            });
        }

        private void PopulateProcurementModalOptions()
        {
            ViewBag.Products = _inventoryService.GetProducts();
            ViewBag.ProcurementBudgetOptions = _inventoryService.GetProcurementBudgetOptions();
            ViewBag.ProcurementReorderSuggestions = _inventoryService.GetReorderSuggestions();
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

        private static (DateTime? StartDate, DateTime? EndDate) ResolveDateRange(
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
                "custom" => (startDate?.Date, endDate?.Date),
                _ => (startDate?.Date, endDate?.Date)
            };
        }

        private static int NormalizePageSize(int pageSize)
        {
            return AllowedPageSizes.Contains(pageSize) ? pageSize : 10;
        }

        private static string NormalizeProcurementSortBy(string? sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
            {
                return "date_desc";
            }

            return sortBy.Trim().ToLowerInvariant() switch
            {
                "date_asc" => "date_asc",
                "date_desc" => "date_desc",
                "po_asc" => "po_asc",
                "po_desc" => "po_desc",
                "supplier_asc" => "supplier_asc",
                "supplier_desc" => "supplier_desc",
                "total_asc" => "total_asc",
                "total_desc" => "total_desc",
                "status_asc" => "status_asc",
                "status_desc" => "status_desc",
                _ => "date_desc"
            };
        }

        private static string NormalizeProcurementBudgetFilter(string? budgetFilter)
        {
            if (string.IsNullOrWhiteSpace(budgetFilter))
            {
                return "all";
            }

            return budgetFilter.Trim().ToLowerInvariant() switch
            {
                "linked" => "linked",
                "unlinked" => "unlinked",
                _ => "all"
            };
        }

        private static string NormalizeProcurementStatusFilter(string? statusFilter)
        {
            if (string.IsNullOrWhiteSpace(statusFilter))
            {
                return "all";
            }

            return statusFilter.Trim().ToLowerInvariant() switch
            {
                "draft" => "draft",
                "approved" => "approved",
                "partiallyreceived" => "partiallyreceived",
                "received" => "received",
                "cancelled" => "cancelled",
                _ => "all"
            };
        }

        private static IEnumerable<ProcurementViewModel> FilterProcurements(
            IEnumerable<ProcurementViewModel> procurements,
            string budgetFilter,
            string statusFilter)
        {
            var query = procurements;
            if (string.Equals(budgetFilter, "linked", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(procurement => procurement.BudgetId.HasValue);
            }
            else if (string.Equals(budgetFilter, "unlinked", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(procurement => !procurement.BudgetId.HasValue);
            }

            if (!string.Equals(statusFilter, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(procurement =>
                    string.Equals(procurement.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            return query;
        }

        private static IEnumerable<ProcurementViewModel> SortProcurements(
            IEnumerable<ProcurementViewModel> procurements,
            string sortBy)
        {
            return sortBy switch
            {
                "date_asc" => procurements
                    .OrderBy(procurement => procurement.PurchaseDate)
                    .ThenByDescending(procurement => procurement.PurchaseOrderId ?? 0),
                "po_asc" => procurements
                    .OrderBy(procurement => procurement.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "po_desc" => procurements
                    .OrderByDescending(procurement => procurement.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "supplier_asc" => procurements
                    .OrderBy(procurement => procurement.SupplierName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "supplier_desc" => procurements
                    .OrderByDescending(procurement => procurement.SupplierName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "total_asc" => procurements
                    .OrderBy(procurement => procurement.TotalProcurementCost)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "total_desc" => procurements
                    .OrderByDescending(procurement => procurement.TotalProcurementCost)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "status_asc" => procurements
                    .OrderBy(procurement => procurement.Status, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                "status_desc" => procurements
                    .OrderByDescending(procurement => procurement.Status, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(procurement => procurement.PurchaseDate),
                _ => procurements
                    .OrderByDescending(procurement => procurement.PurchaseDate)
                    .ThenByDescending(procurement => procurement.PurchaseOrderId ?? 0)
            };
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

        private bool IsSuperAdminScopedReadOnly()
        {
            return _tenantContext.IsSuperAdmin &&
                   (!_tenantContext.HasOwnerScope || !_tenantContext.CanEditOwnerWorkspace);
        }

        private bool CanManageProcurementWorkflowActions()
        {
            return User.IsInRole("Owner") || User.IsInRole("SuperAdmin");
        }

        private static List<InventoryHistoryTypeOptionViewModel> BuildHistoryTypeOptions()
        {
            return new List<InventoryHistoryTypeOptionViewModel>
            {
                new() { Value = "all", Label = "All events" },
                new() { Value = "stock_in", Label = "Stock in" },
                new() { Value = "stock_out", Label = "Stock out" },
                new() { Value = "procurement", Label = "Procurement" }
            };
        }

        private static string NormalizeHistoryType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "all";
            }

            return type.Trim().ToLowerInvariant() switch
            {
                "all" => "all",
                "stock_in" => "stock_in",
                "stock_out" => "stock_out",
                "procurement" => "procurement",
                _ => "all"
            };
        }

        private static List<InventoryHistoryItemViewModel> ApplyHistoryTypeFilter(
            List<InventoryHistoryItemViewModel> items,
            string normalizedType)
        {
            if (string.Equals(normalizedType, "all", StringComparison.OrdinalIgnoreCase))
            {
                return items;
            }

            return items
                .Where(item => string.Equals(item.EventTypeKey, normalizedType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static InventoryHistoryItemViewModel MapInventoryMovementToHistoryItem(InventoryMovement movement)
        {
            var (eventTypeKey, eventTypeLabel) = movement.MovementType switch
            {
                "StockIn" => ("stock_in", "Stock In"),
                "StockOut" => ("stock_out", "Stock Out"),
                "ProcurementPlan" => ("procurement", "Procurement"),
                _ => ("system", string.IsNullOrWhiteSpace(movement.MovementType) ? "System" : movement.MovementType)
            };

            var quantityDelta = movement.QuantityDelta;
            if (string.Equals(movement.MovementType, "StockIn", StringComparison.OrdinalIgnoreCase))
            {
                quantityDelta = Math.Abs(quantityDelta);
            }
            else if (string.Equals(movement.MovementType, "StockOut", StringComparison.OrdinalIgnoreCase))
            {
                quantityDelta = -Math.Abs(quantityDelta);
            }

            return new InventoryHistoryItemViewModel
            {
                TimestampUtc = movement.OccurredAtUtc,
                RelativeTime = FormatRelativeTime(movement.OccurredAtUtc),
                EventTypeKey = eventTypeKey,
                EventTypeLabel = eventTypeLabel,
                ProductName = movement.Product?.ProductName ?? $"Product #{movement.ProductID}",
                Sku = movement.Product?.Sku ?? string.Empty,
                QuantityDelta = quantityDelta,
                UnitCost = movement.UnitCostAtMovement,
                ActorDisplay = FormatActorRole(movement.PerformedByUser?.Role),
                Reference = BuildMovementReference(movement),
                Details = BuildMovementDetails(movement)
            };
        }

        private static InventoryHistoryItemViewModel MapInventoryAuditLogToHistoryItem(SystemLog log)
        {
            var normalizedAction = StripInventoryActionPrefix(log.Action);
            var (eventTypeKey, eventTypeLabel) = ClassifyInventoryEventTypeFromAuditAction(normalizedAction);

            return new InventoryHistoryItemViewModel
            {
                TimestampUtc = log.Timestamp,
                RelativeTime = FormatRelativeTime(log.Timestamp),
                EventTypeKey = eventTypeKey,
                EventTypeLabel = eventTypeLabel,
                ProductName = "-",
                Sku = string.Empty,
                QuantityDelta = null,
                UnitCost = null,
                ActorDisplay = FormatActorRole(log.User?.Role),
                Reference = "SystemLog",
                Details = string.IsNullOrWhiteSpace(normalizedAction)
                    ? "Inventory event logged."
                    : normalizedAction
            };
        }

        private static string FormatActorRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "System";
            }

            var normalized = role.Trim();
            return normalized switch
            {
                "SuperAdmin" => "SuperAdmin",
                _ => normalized
            };
        }

        private static (string EventTypeKey, string EventTypeLabel) ClassifyInventoryEventTypeFromAuditAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return ("system", "System");
            }

            var normalizedAction = action.ToLowerInvariant();
            if (normalizedAction.Contains("product"))
            {
                return ("product", "Product");
            }

            if (normalizedAction.Contains("procurement"))
            {
                return ("procurement", "Procurement");
            }

            if (normalizedAction.Contains("stock in"))
            {
                return ("stock_in", "Stock In");
            }

            if (normalizedAction.Contains("stock out"))
            {
                return ("stock_out", "Stock Out");
            }

            return ("system", "System");
        }

        private static string BuildMovementReference(InventoryMovement movement)
        {
            var referenceType = movement.ReferenceType?.Trim() ?? string.Empty;
            var referenceId = movement.ReferenceId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(referenceType) && string.IsNullOrWhiteSpace(referenceId))
            {
                return "-";
            }

            if (string.IsNullOrWhiteSpace(referenceType))
            {
                return referenceId;
            }

            if (string.IsNullOrWhiteSpace(referenceId))
            {
                return referenceType;
            }

            return $"{referenceType}: {referenceId}";
        }

        private static string BuildMovementDetails(InventoryMovement movement)
        {
            var details = new List<string>();

            if (!string.IsNullOrWhiteSpace(movement.Reason))
            {
                details.Add(movement.Reason.Trim());
            }

            if (!string.IsNullOrWhiteSpace(movement.PartnerName))
            {
                details.Add($"Partner: {movement.PartnerName.Trim()}");
            }

            if (details.Count == 0)
            {
                return "Inventory movement logged.";
            }

            return string.Join(" | ", details);
        }

        private static string StripInventoryActionPrefix(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            const string prefix = "Inventory:";
            return action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? action[prefix.Length..].Trim()
                : action.Trim();
        }

        private static string FormatRelativeTime(DateTime timestampUtc)
        {
            var elapsed = DateTime.UtcNow - timestampUtc;
            if (elapsed.TotalMinutes < 1)
            {
                return "just now";
            }

            if (elapsed.TotalHours < 1)
            {
                return $"{(int)elapsed.TotalMinutes}m ago";
            }

            if (elapsed.TotalDays < 1)
            {
                return $"{(int)elapsed.TotalHours}h ago";
            }

            if (elapsed.TotalDays < 30)
            {
                return $"{(int)elapsed.TotalDays}d ago";
            }

            return BusinessTime.ConvertUtcToBusinessTime(timestampUtc).ToString("MMM dd, yyyy");
        }

        private void TryAddInventoryAuditLog(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return;
            }

            var actorUserId = _tenantContext.CurrentUserId ?? ownerUserId.Value;
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId,
                OwnerUserID = ownerUserId.Value,
                Action = $"Inventory: {action.Trim()}",
                Timestamp = DateTime.UtcNow
            });
            _context.SaveChanges();
        }

        private static string BuildProductAuditDescriptor(string? productName, string? sku)
        {
            var normalizedName = string.IsNullOrWhiteSpace(productName) ? "Unnamed" : productName.Trim();
            var normalizedSku = string.IsNullOrWhiteSpace(sku) ? "N/A" : sku.Trim();
            return $"Product: {normalizedName} (SKU: {normalizedSku})";
        }

        private void PopulateProductSelectionOptions(ProductViewModel model)
        {
            var (categories, brands) = _inventoryService.GetProductSelectionOptions();
            model.CategoryOptions = categories;
            model.BrandOptions = brands;
        }

        private static void NormalizeCustomCategoryAndBrand(ProductViewModel model)
        {
            const string othersValue = "__OTHER__";

            if (string.Equals(model.Category, othersValue, StringComparison.Ordinal))
            {
                model.Category = model.NewCategory?.Trim() ?? string.Empty;
            }

            if (string.Equals(model.Brand, othersValue, StringComparison.Ordinal))
            {
                model.Brand = model.NewBrand?.Trim() ?? string.Empty;
            }
        }

        private static bool IsReservedCategoryName(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
            }

            var normalized = category.Trim();
            return string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "All products", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "__OTHER__", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryValidateProductUpsertForm(ProductViewModel model, bool isEdit, out string message)
        {
            PruneProductUpsertModelState(isEdit);

            if (isEdit)
            {
                if (string.IsNullOrWhiteSpace(model.Id))
                {
                    ModelState.AddModelError(nameof(ProductViewModel.Id), "Product ID is required.");
                }
                else if (!int.TryParse(model.Id, out _))
                {
                    ModelState.AddModelError(nameof(ProductViewModel.Id), "Invalid product ID.");
                }
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(ProductViewModel.Name), "Product Name is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Sku))
            {
                ModelState.AddModelError(nameof(ProductViewModel.Sku), "SKU is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Category))
            {
                ModelState.AddModelError(nameof(ProductViewModel.Category), "Category is required.");
            }

            if (string.IsNullOrWhiteSpace(model.Brand))
            {
                ModelState.AddModelError(nameof(ProductViewModel.Brand), "Brand is required.");
            }

            if (model.StockQuantity < 0)
            {
                ModelState.AddModelError(nameof(ProductViewModel.StockQuantity), "Initial stock cannot be negative.");
            }

            if (model.UnitCost < 0m)
            {
                ModelState.AddModelError(nameof(ProductViewModel.UnitCost), "Unit cost cannot be negative.");
            }

            if (model.RetailPrice < 0m)
            {
                ModelState.AddModelError(nameof(ProductViewModel.RetailPrice), "Retail price cannot be negative.");
            }

            if (model.EbayLivePrice.HasValue && model.EbayLivePrice.Value < 0m)
            {
                ModelState.AddModelError(nameof(ProductViewModel.EbayLivePrice), "Suggested price cannot be negative.");
            }

            if (ModelState.IsValid)
            {
                message = string.Empty;
                return true;
            }

            var errors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                {
                    var field = string.IsNullOrWhiteSpace(entry.Key) ? "Form" : entry.Key;
                    var errorMessage = string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "Invalid value."
                        : error.ErrorMessage;
                    return $"{field}: {errorMessage}";
                }))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            var actionLabel = isEdit ? "update" : "add";
            message = errors.Count > 0
                ? $"Unable to {actionLabel} product. {string.Join(" ", errors)}"
                : $"Unable to {actionLabel} product. Please review the form values.";
            return false;
        }

        private void PruneProductUpsertModelState(bool isEdit)
        {
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                nameof(ProductViewModel.Name),
                nameof(ProductViewModel.Sku),
                nameof(ProductViewModel.Category),
                nameof(ProductViewModel.Brand),
                nameof(ProductViewModel.StockQuantity),
                nameof(ProductViewModel.UnitCost),
                nameof(ProductViewModel.RetailPrice),
                nameof(ProductViewModel.EbayLivePrice)
            };

            if (isEdit)
            {
                allowedKeys.Add(nameof(ProductViewModel.Id));
            }

            foreach (var key in ModelState.Keys.ToList())
            {
                if (!allowedKeys.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }
        }

        // Action of FetchSuggestedPrice
        [HttpGet]
        public async Task<IActionResult> FetchSuggestedPrice(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Query parameter is required.");
            }

            var quote = await _ebayBrowseService.GetMarketPriceAsync(query);
            if (quote == null || quote.Price <= 0)
            {
                return NotFound("No suggested price found for the given query.");
            }

            return Json(new { price = quote.Price, currency = quote.Currency, source = quote.Source, topPrices = quote.TopPrices });
        }
    }
}

