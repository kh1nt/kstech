using kstech.Configuration;
using kstech.Data;
using kstech.Models;
using kstech.Models.Entities;
using kstech.Models.ViewModels;
using kstech.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using System.Globalization;
using System.Net;

namespace kstech.Services
{
    public interface IInventoryService
    {
        List<ProductViewModel> GetProducts(bool showArchived = false);
        ProductViewModel? GetProduct(string id, bool includeArchived = false);
        void AddProduct(ProductViewModel product);
        void UpdateProduct(ProductViewModel product);
        bool UpdateProductCondition(string id, int damagedQuantity, string conditionStatus, string? conditionNotes, DateTime? lastConditionCheckUtc);
        (List<string> Categories, List<string> Brands) GetProductSelectionOptions();
        void ArchiveProduct(string id);
        void UnarchiveProduct(string id);

        List<ProcurementViewModel> GetProcurements();
        List<ProcurementBudgetOptionViewModel> GetProcurementBudgetOptions();
        List<ReorderSuggestionViewModel> GetReorderSuggestions(int maxItems = 8);
        ProcurementActionResult CreateAutoReorderDraftPurchaseOrder(int maxItems = 5, string? supplierName = null);
        ProcurementActionResult AddProcurement(ProcurementViewModel procurement);
        ProcurementActionResult DeleteDraftProcurement(int purchaseOrderId);
        ProcurementActionResult ApproveProcurement(int purchaseOrderId);
        ProcurementActionResult ReceiveProcurement(int purchaseOrderId, IEnumerable<ProcurementReceiveLineInput>? receiveLines = null);

        List<SlowMoverSuggestionViewModel> GetSlowMoverSuggestions(int maxItems = 6, int staleDays = 45);
        InventoryAutomationActionResult QueueSlowMoverPromotion(int productId, int customerLimit = 200);

        InventoryViewModel GetInventoryStats(
            bool showArchived = false,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            int page = 1,
            int pageSize = 10);
        InventoryDashboardViewModel GetDashboardStats(DateTime? startDate = null, DateTime? endDate = null);

        List<SalesDataPoint> GetSalesHistory(int months);
        List<SalesDataPoint> GetSalesChartData(DateTime start, DateTime end);
        List<decimal> GetSalesByCategory(DateTime start, DateTime end, out List<string> labels);
        decimal GetTotalSales(DateTime start, DateTime end);
        List<RecentOrderViewModel> GetRecentOrders(int count, DateTime? startDate = null, DateTime? endDate = null);
        List<ProductViewModel> GetTopSellingProducts(int count, DateTime? startDate = null, DateTime? endDate = null);

        // Added for seeding
        void SeedData();
    }

    public class InventoryService : IInventoryService
    {
        private const string ArchivedMarketPriceSource = "Archived";
        private const string PurchaseOrderStatusDraft = "Draft";
        private const string PurchaseOrderStatusApproved = "Approved";
        private const string PurchaseOrderStatusPartiallyReceived = "PartiallyReceived";
        private const string PurchaseOrderStatusReceived = "Received";
        private const string PurchaseOrderStatusCancelled = "Cancelled";
        private const string BudgetEventTypeReserve = "Reserve";
        private const string BudgetEventTypeSpend = "Spend";
        private const string BudgetEventReferenceTypePurchaseOrder = "PurchaseOrder";
        private const string ProcurementReferenceType = "PurchaseOrder";
        private const string ProcurementPlanMovementType = "ProcurementPlan";
        private const string ProcurementPlanReasonPrefix = "PROC-PLAN";
        private static readonly int[] AllowedPageSizes = { 10, 20, 50 };
        private readonly ApplicationDbContext _context;
        private readonly IInventoryControlService _inventoryControlService;
        private readonly ITenantContext _tenantContext;
        private readonly int _lowStockThreshold;
        private bool? _hasPurchaseOrderTables;

        public InventoryService(
            ApplicationDbContext context,
            IInventoryControlService inventoryControlService,
            ITenantContext tenantContext,
            IOptions<InventoryRuleOptions> inventoryRuleOptions)
        {
            _context = context;
            _inventoryControlService = inventoryControlService;
            _tenantContext = tenantContext;
            _lowStockThreshold = Math.Max(1, inventoryRuleOptions.Value.LowStockThreshold);
        }

        public void SeedData()
        {
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return;
            }

            if (_context.Products.Any(product => product.OwnerUserID == ownerUserId.Value))
            {
                return;
            }

            var products = new List<Product>
            {
                new Product
                {
                    OwnerUserID = ownerUserId.Value,
                    ProductName = "NVIDIA RTX 4080",
                    Sku = "NV-4080-OC",
                    CategoryName = "GPU",
                    Brand = "NVIDIA",
                    Description = "16GB GDDR6X, Ada Lovelace Arch",
                    StockQuantity = 42,
                    CostPrice = 899.00m,
                    SellingPrice = 1199.00m,
                    MarketPrice = 1150.00m,
                    MarketPriceSource = "Seed",
                    ImageUrl = string.Empty
                },
                new Product
                {
                    OwnerUserID = ownerUserId.Value,
                    ProductName = "Intel Core i9-13900K",
                    Sku = "INT-I9-13K",
                    CategoryName = "CPU",
                    Brand = "Intel",
                    Description = "24 Cores, 32 Threads, up to 5.8 GHz",
                    StockQuantity = 5,
                    CostPrice = 480.00m,
                    SellingPrice = 589.00m,
                    MarketPrice = 575.00m,
                    MarketPriceSource = "Seed",
                    ImageUrl = string.Empty
                },
                new Product
                {
                    OwnerUserID = ownerUserId.Value,
                    ProductName = "Corsair Vengeance 32GB",
                    Sku = "CR-DDR5-32",
                    CategoryName = "RAM",
                    Brand = "Corsair",
                    Description = "DDR5 5600MHz CL36",
                    StockQuantity = 120,
                    CostPrice = 95.00m,
                    SellingPrice = 149.99m,
                    MarketPrice = 145.00m,
                    MarketPriceSource = "Seed",
                    ImageUrl = string.Empty
                },
                new Product
                {
                    OwnerUserID = ownerUserId.Value,
                    ProductName = "Samsung 980 Pro 2TB",
                    Sku = "SS-980P-2T",
                    CategoryName = "Storage",
                    Brand = "Samsung",
                    Description = "PCIe Gen 4.0 x4, NVMe 1.3c",
                    StockQuantity = 0,
                    CostPrice = 120.00m,
                    SellingPrice = 179.99m,
                    MarketPrice = 170.00m,
                    MarketPriceSource = "Seed",
                    ImageUrl = string.Empty
                },
                new Product
                {
                    OwnerUserID = ownerUserId.Value,
                    ProductName = "ASUS ROG Maximus Z790",
                    Sku = "AS-Z790-H",
                    CategoryName = "Motherboard",
                    Brand = "ASUS",
                    Description = "LGA 1700, DDR5, PCIe 5.0",
                    StockQuantity = 18,
                    CostPrice = 550.00m,
                    SellingPrice = 699.99m,
                    MarketPrice = 680.00m,
                    MarketPriceSource = "Seed",
                    ImageUrl = string.Empty
                }
            };

            _context.Products.AddRange(products);
            _context.SaveChanges();
        }

        public List<ProductViewModel> GetProducts(bool showArchived = false)
        {
            var products = ApplyOwnerFilter(_context.Products)
                .Where(p => showArchived
                    ? p.MarketPriceSource == ArchivedMarketPriceSource
                    : p.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList();
            return products.Select(MapToViewModel).ToList();
        }

        public ProductViewModel? GetProduct(string id, bool includeArchived = false)
        {
            if (!int.TryParse(id, out int productId)) return null;

            var product = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(p =>
                    p.ProductID == productId &&
                    (includeArchived || p.MarketPriceSource != ArchivedMarketPriceSource));
            return product != null ? MapToViewModel(product) : null;
        }

        public void AddProduct(ProductViewModel productVM)
        {
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return;
            }

            var categoryName = NormalizeCategoryName(productVM.Category);
            var initialStockQuantity = Math.Max(0, productVM.StockQuantity);

            var marketReference = productVM.EbayLivePrice.GetValueOrDefault() > 0m
                ? productVM.EbayLivePrice.GetValueOrDefault()
                : productVM.RetailPrice;

            var product = new Product
            {
                OwnerUserID = ownerUserId.Value,
                ProductName = productVM.Name,
                Sku = productVM.Sku,
                CategoryName = categoryName,
                Brand = productVM.Brand,
                Description = productVM.Description,
                StockQuantity = 0,
                DamagedQuantity = 0,
                CostPrice = productVM.UnitCost,
                SellingPrice = productVM.RetailPrice,
                MarketPrice = marketReference,
                MarketPriceSource = productVM.EbayLivePrice.GetValueOrDefault() > 0m ? "Manual" : string.Empty,
                ConditionStatus = "Good",
                ConditionNotes = string.Empty,
                LastConditionCheckUtc = null,
                ImageUrl = string.Empty
            };

            using var transaction = _context.Database.BeginTransaction();

            _context.Products.Add(product);
            _context.SaveChanges();

            if (initialStockQuantity > 0)
            {
                _inventoryControlService.ApplyStockIn(
                    product,
                    initialStockQuantity,
                    product.CostPrice,
                    "Opening balance",
                    "Initial stock on product creation",
                    "OpeningBalance",
                    $"PRODUCT-{product.ProductID}",
                    _tenantContext.CurrentUserId);

                _context.SaveChanges();
            }

            transaction.Commit();
        }

        public void UpdateProduct(ProductViewModel productVM)
        {
            if (!int.TryParse(productVM.Id, out int productId)) return;
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return;
            }

            var categoryName = NormalizeCategoryName(productVM.Category);

            var existing = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(p =>
                    p.ProductID == productId &&
                    p.MarketPriceSource != ArchivedMarketPriceSource);
            if (existing != null)
            {
                existing.ProductName = productVM.Name;
                existing.Sku = productVM.Sku;
                existing.CategoryName = categoryName;

                existing.Brand = productVM.Brand;
                existing.Description = productVM.Description;
                existing.StockQuantity = Math.Max(0, productVM.StockQuantity);
                existing.CostPrice = productVM.UnitCost;
                existing.SellingPrice = productVM.RetailPrice;

                if (productVM.EbayLivePrice.HasValue && productVM.EbayLivePrice.Value > 0m)
                {
                    existing.MarketPrice = productVM.EbayLivePrice.Value;
                    existing.MarketPriceSource = "Manual";
                }

                _context.SaveChanges();
            }
        }

        public bool UpdateProductCondition(
            string id,
            int damagedQuantity,
            string conditionStatus,
            string? conditionNotes,
            DateTime? lastConditionCheckUtc)
        {
            if (!int.TryParse(id, out var productId))
            {
                return false;
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return false;
            }

            var existing = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(product =>
                    product.ProductID == productId &&
                    product.MarketPriceSource != ArchivedMarketPriceSource);
            if (existing == null)
            {
                return false;
            }

            var normalizedDamagedQuantity = Math.Max(0, damagedQuantity);
            var normalizedConditionStatus = NormalizeConditionStatus(conditionStatus);
            var normalizedConditionCheckUtc = NormalizeConditionCheckUtc(
                lastConditionCheckUtc,
                normalizedDamagedQuantity,
                normalizedConditionStatus);

            existing.DamagedQuantity = normalizedDamagedQuantity;
            existing.ConditionStatus = normalizedConditionStatus;
            existing.ConditionNotes = NormalizeConditionNotes(conditionNotes);
            existing.LastConditionCheckUtc = normalizedConditionCheckUtc;

            _context.SaveChanges();
            return true;
        }

        public (List<string> Categories, List<string> Brands) GetProductSelectionOptions()
        {
            var rawCategories = ApplyOwnerFilter(_context.Products)
                .AsNoTracking()
                .Where(product => product.MarketPriceSource != ArchivedMarketPriceSource)
                .Select(product => product.CategoryName)
                .Where(categoryName => !string.IsNullOrWhiteSpace(categoryName))
                .Distinct()
                .ToList();

            var categories = rawCategories
                .Select(NormalizeCategoryName)
                .Where(categoryName => !string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(categoryName => categoryName)
                .ToList();

            var brands = ApplyOwnerFilter(_context.Products)
                .AsNoTracking()
                .Where(product =>
                    product.MarketPriceSource != ArchivedMarketPriceSource &&
                    !string.IsNullOrWhiteSpace(product.Brand))
                .Select(product => product.Brand.Trim())
                .Distinct()
                .OrderBy(brand => brand)
                .ToList();

            return (categories, brands);
        }

        public void ArchiveProduct(string id)
        {
            if (!int.TryParse(id, out int productId)) return;
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return;
            }

            var product = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(p => p.ProductID == productId);
            if (product == null || product.MarketPriceSource == ArchivedMarketPriceSource)
            {
                return;
            }

            product.MarketPriceSource = ArchivedMarketPriceSource;
            _context.SaveChanges();
        }

        public void UnarchiveProduct(string id)
        {
            if (!int.TryParse(id, out int productId)) return;
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return;
            }

            var product = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(p => p.ProductID == productId);
            if (product == null || product.MarketPriceSource != ArchivedMarketPriceSource)
            {
                return;
            }

            product.MarketPriceSource = string.Empty;
            _context.SaveChanges();
        }

        public List<ProcurementViewModel> GetProcurements()
        {
            var relationalProcurements = HasPurchaseOrderTables()
                ? GetRelationalProcurements()
                : new List<ProcurementViewModel>();
            var movementProcurements = GetMovementBasedProcurements();

            return relationalProcurements
                .Concat(movementProcurements)
                .OrderByDescending(procurement => procurement.PurchaseDate)
                .ToList();
        }

        public List<ProcurementBudgetOptionViewModel> GetProcurementBudgetOptions()
        {
            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return new List<ProcurementBudgetOptionViewModel>();
            }

            var today = BusinessTime.Today;
            var budgets = _context.FinancialBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.OwnerUserID == ownerUserId.Value &&
                    budget.Status == "Active")
                .OrderByDescending(budget => budget.PeriodEndDateLocal)
                .ThenByDescending(budget => budget.BudgetID)
                .ToList()
                .GroupBy(budget => new
                {
                    MonthStart = new DateTime(budget.PeriodStartDateLocal.Year, budget.PeriodStartDateLocal.Month, 1)
                })
                .Select(group => group
                    .OrderByDescending(budget => budget.UpdatedAtUtc)
                    .ThenByDescending(budget => budget.BudgetID)
                    .First())
                .ToList();

            return budgets
                .Select(budget =>
                {
                    var committedAmount = CalculateCommittedAmountForBudget(ownerUserId.Value, budget.BudgetID);
                    var availableAmount = Math.Round(
                        budget.BudgetAmount - committedAmount,
                        2,
                        MidpointRounding.AwayFromZero);

                    return new ProcurementBudgetOptionViewModel
                    {
                        BudgetId = budget.BudgetID,
                        Label = $"{budget.PeriodStartDateLocal:MMM dd, yyyy} - {budget.PeriodEndDateLocal:MMM dd, yyyy}",
                        BudgetAmount = budget.BudgetAmount,
                        CommittedAmount = committedAmount,
                        AvailableAmount = availableAmount,
                        IsSuggested = budget.PeriodStartDateLocal <= today && budget.PeriodEndDateLocal >= today
                    };
                })
                .OrderByDescending(option => option.IsSuggested)
                .ThenByDescending(option => option.BudgetId)
                .ToList();
        }

        public List<ReorderSuggestionViewModel> GetReorderSuggestions(int maxItems = 8)
        {
            var normalizedMax = Math.Clamp(maxItems, 1, 25);
            var reorderLevel = Math.Max(1, _lowStockThreshold);

            var suggestions = ApplyOwnerFilter(_context.Products)
                .AsNoTracking()
                .Where(product => product.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList()
                .Select(product =>
                {
                    var stockStatus = CalculateStockStatus(product.StockQuantity, reorderLevel);
                    var targetStock = Math.Max(reorderLevel * 2, reorderLevel + 3);
                    var suggestedOrderQuantity = Math.Max(0, targetStock - product.StockQuantity);
                    var estimatedLineCost = Math.Round(
                        suggestedOrderQuantity * Math.Max(0m, product.CostPrice),
                        2,
                        MidpointRounding.AwayFromZero);

                    return new ReorderSuggestionViewModel
                    {
                        ProductId = product.ProductID,
                        ProductName = product.ProductName,
                        Sku = product.Sku,
                        Category = NormalizeCategoryName(product.CategoryName),
                        Brand = product.Brand,
                        CurrentStock = product.StockQuantity,
                        ReorderLevel = reorderLevel,
                        SuggestedOrderQuantity = suggestedOrderQuantity,
                        UnitCost = product.CostPrice,
                        EstimatedLineCost = estimatedLineCost,
                        StockStatus = stockStatus
                    };
                })
                .Where(item =>
                    item.SuggestedOrderQuantity > 0 &&
                    (string.Equals(item.StockStatus, "Out of Stock", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.StockStatus, "Low Stock", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => string.Equals(item.StockStatus, "Out of Stock", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.CurrentStock)
                .ThenByDescending(item => item.EstimatedLineCost)
                .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                .Take(normalizedMax)
                .ToList();

            return suggestions;
        }

        public ProcurementActionResult CreateAutoReorderDraftPurchaseOrder(int maxItems = 5, string? supplierName = null)
        {
            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var suggestions = GetReorderSuggestions(Math.Clamp(maxItems, 1, 12));
            if (!suggestions.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "No low-stock or out-of-stock items need auto reorder right now."
                };
            }

            var activeBudgetId = ResolveActiveBudget(ownerUserId.Value, BusinessTime.Today)?.BudgetID;
            var draftSupplierName = string.IsNullOrWhiteSpace(supplierName)
                ? "Auto Reorder Suggestion"
                : supplierName.Trim();

            var procurement = new ProcurementViewModel
            {
                SupplierName = draftSupplierName,
                BudgetId = activeBudgetId,
                Items = suggestions.Select(item => new ProcurementItemViewModel
                {
                    ProductId = item.ProductId.ToString(CultureInfo.InvariantCulture),
                    ProductName = item.ProductName,
                    QuantityOrdered = item.SuggestedOrderQuantity,
                    QuantityReceived = 0,
                    CostPerItem = item.UnitCost > 0m ? item.UnitCost : 0m
                }).ToList()
            };

            var result = AddProcurement(procurement);
            if (!result.Succeeded)
            {
                return result;
            }

            var totalEstimate = suggestions.Sum(item => item.EstimatedLineCost);
            result.Message = $"{result.Message} Auto reorder used {suggestions.Count} item(s), estimated total {Math.Round(totalEstimate, 2, MidpointRounding.AwayFromZero):C}.";
            return result;
        }

        private List<ProcurementViewModel> GetRelationalProcurements()
        {
            var purchaseOrderQuery = _context.PurchaseOrders
                .AsNoTracking()
                .Include(purchaseOrder => purchaseOrder.Budget)
                .Include(purchaseOrder => purchaseOrder.Lines)
                .ThenInclude(line => line.Product)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                purchaseOrderQuery = purchaseOrderQuery.Where(purchaseOrder => purchaseOrder.OwnerUserID == ownerUserId);
            }

            return purchaseOrderQuery
                .OrderByDescending(purchaseOrder => purchaseOrder.CreatedAtUtc)
                .ToList()
                .Select(purchaseOrder =>
                {
                    var orderedAmount = CalculateOrderedAmount(purchaseOrder.Lines);
                    var actualExpenseAmount = CalculateActualExpenseAmount(purchaseOrder.Lines);
                    var remainingAmount = CalculateRemainingAmount(purchaseOrder.Lines);
                    var normalizedStatus = NormalizePurchaseOrderStatus(purchaseOrder.Status);
                    var reservedAmount = purchaseOrder.BudgetID.HasValue && IsReservationStatus(normalizedStatus)
                        ? remainingAmount
                        : 0m;

                    return new ProcurementViewModel
                    {
                        PurchaseOrderId = purchaseOrder.PurchaseOrderID,
                        Id = ToDisplayProcurementNumber(purchaseOrder.PurchaseOrderNumber),
                        SupplierName = string.IsNullOrWhiteSpace(purchaseOrder.SupplierName)
                            ? "Unknown supplier"
                            : purchaseOrder.SupplierName,
                        PurchaseDate = purchaseOrder.CreatedAtUtc,
                        TotalProcurementCost = orderedAmount,
                        ReservedAmount = reservedAmount,
                        ActualExpenseAmount = actualExpenseAmount,
                        RemainingReservationAmount = remainingAmount,
                        Status = normalizedStatus,
                        BudgetId = purchaseOrder.BudgetID,
                        BudgetLabel = purchaseOrder.Budget != null
                            ? $"{purchaseOrder.Budget.PeriodStartDateLocal:MMM dd, yyyy} - {purchaseOrder.Budget.PeriodEndDateLocal:MMM dd, yyyy}"
                            : "No linked budget",
                        CanApprove = string.Equals(normalizedStatus, PurchaseOrderStatusDraft, StringComparison.OrdinalIgnoreCase),
                        CanReceive =
                            string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase),
                        Items = purchaseOrder.Lines
                            .OrderBy(line => line.PurchaseOrderLineID)
                            .Select(line => new ProcurementItemViewModel
                            {
                                PurchaseOrderLineId = line.PurchaseOrderLineID,
                                ProductId = line.ProductID.ToString(CultureInfo.InvariantCulture),
                                ProductName = line.Product?.ProductName ?? $"Product #{line.ProductID}",
                                QuantityOrdered = line.QuantityOrdered,
                                QuantityReceived = line.QuantityReceived,
                                CostPerItem = line.UnitCost
                            })
                            .ToList()
                    };
                })
                .ToList();
        }

        private List<ProcurementViewModel> GetMovementBasedProcurements()
        {
            var planQuery = _context.InventoryMovements
                .AsNoTracking()
                .Include(movement => movement.Product)
                .Where(movement =>
                    movement.ReferenceType == ProcurementReferenceType &&
                    movement.MovementType == ProcurementPlanMovementType)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                planQuery = planQuery.Where(movement => movement.OwnerUserID == ownerUserId);
            }

            var planRows = planQuery
                .OrderByDescending(movement => movement.OccurredAtUtc)
                .ToList();

            var groupedPlans = planRows
                .GroupBy(movement => movement.ReferenceId)
                .Select(group =>
                {
                    var lines = group.OrderBy(movement => movement.MovementID).ToList();
                    var metadata = ParseProcurementPlanReason(lines.First().Reason);
                    return new
                    {
                        ReferenceId = group.Key,
                        Lines = lines,
                        Metadata = metadata
                    };
                })
                .ToList();

            var budgetIds = groupedPlans
                .Select(group => group.Metadata.BudgetId)
                .Where(budgetId => budgetId.HasValue)
                .Select(budgetId => budgetId!.Value)
                .Distinct()
                .ToList();

            var budgetQuery = _context.FinancialBudgets
                .AsNoTracking()
                .Where(budget => budgetIds.Contains(budget.BudgetID))
                .AsQueryable();
            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                budgetQuery = budgetQuery.Where(budget => budget.OwnerUserID == ownerUserId);
            }

            var budgetLookup = budgetQuery.ToDictionary(
                budget => budget.BudgetID,
                budget => $"{budget.PeriodStartDateLocal:MMM dd, yyyy} - {budget.PeriodEndDateLocal:MMM dd, yyyy}");

            var planProcurements = groupedPlans
                .Select(group =>
                {
                    var normalizedStatus = NormalizePurchaseOrderStatus(group.Metadata.Status);
                    var remainingAmount = CalculateRemainingAmountForPlanLines(group.Lines);
                    var actualExpenseAmount = CalculateActualExpenseAmountForPlanLines(group.Lines);
                    var referenceId = string.IsNullOrWhiteSpace(group.ReferenceId)
                        ? $"PROC-{group.Lines.Min(movement => movement.MovementID)}"
                        : group.ReferenceId;

                    return new ProcurementViewModel
                    {
                        PurchaseOrderId = group.Lines.Min(movement => movement.MovementID),
                        Id = ToDisplayProcurementNumber(referenceId),
                        SupplierName = group.Lines
                            .Select(movement => movement.PartnerName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown supplier",
                        PurchaseDate = group.Lines.Max(movement => movement.OccurredAtUtc),
                        TotalProcurementCost = CalculateOrderedAmountForPlanLines(group.Lines),
                        ReservedAmount = group.Metadata.BudgetId.HasValue && IsReservationStatus(normalizedStatus)
                            ? remainingAmount
                            : 0m,
                        ActualExpenseAmount = actualExpenseAmount,
                        RemainingReservationAmount = remainingAmount,
                        Status = normalizedStatus,
                        BudgetId = group.Metadata.BudgetId,
                        BudgetLabel = group.Metadata.BudgetId.HasValue &&
                                     budgetLookup.TryGetValue(group.Metadata.BudgetId.Value, out var budgetLabel)
                            ? budgetLabel
                            : group.Metadata.BudgetId.HasValue
                                ? $"Budget #{group.Metadata.BudgetId.Value}"
                                : "No linked budget",
                        CanApprove = string.Equals(normalizedStatus, PurchaseOrderStatusDraft, StringComparison.OrdinalIgnoreCase),
                        CanReceive =
                            (string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase)) &&
                            remainingAmount > 0m,
                        Items = group.Lines
                            .Select(movement => new ProcurementItemViewModel
                            {
                                PurchaseOrderLineId = movement.MovementID,
                                ProductId = movement.ProductID.ToString(CultureInfo.InvariantCulture),
                                ProductName = movement.Product?.ProductName ?? $"Product #{movement.ProductID}",
                                QuantityOrdered = GetOrderedQuantity(movement),
                                QuantityReceived = GetReceivedQuantity(movement),
                                CostPerItem = movement.UnitCostAtMovement
                            })
                            .ToList()
                    };
                })
                .ToList();

            var legacyQuery = _context.InventoryMovements
                .AsNoTracking()
                .Include(movement => movement.Product)
                .Where(movement =>
                    movement.ReferenceType == "Procurement" &&
                    movement.MovementType == "StockIn")
                .AsQueryable();
            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                legacyQuery = legacyQuery.Where(movement => movement.OwnerUserID == ownerUserId);
            }

            var legacyProcurements = legacyQuery
                .OrderByDescending(movement => movement.OccurredAtUtc)
                .ToList()
                .GroupBy(movement => movement.ReferenceId)
                .Select(group =>
                {
                    var orderedAmount = Math.Round(
                        group.Sum(movement => Math.Max(0, movement.QuantityDelta) * movement.UnitCostAtMovement),
                        2,
                        MidpointRounding.AwayFromZero);
                    return new ProcurementViewModel
                    {
                        PurchaseOrderId = null,
                        Id = ToDisplayProcurementNumber(group.Key),
                        SupplierName = group
                            .Select(movement => movement.PartnerName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown supplier",
                        PurchaseDate = group.Max(movement => movement.OccurredAtUtc),
                        TotalProcurementCost = orderedAmount,
                        ReservedAmount = 0m,
                        ActualExpenseAmount = orderedAmount,
                        RemainingReservationAmount = 0m,
                        Status = PurchaseOrderStatusReceived,
                        BudgetId = null,
                        BudgetLabel = "Legacy procurement record",
                        CanApprove = false,
                        CanReceive = false,
                        Items = group
                            .OrderBy(movement => movement.MovementID)
                            .Select(movement => new ProcurementItemViewModel
                            {
                                ProductId = movement.ProductID.ToString(CultureInfo.InvariantCulture),
                                ProductName = movement.Product?.ProductName ?? $"Product #{movement.ProductID}",
                                QuantityOrdered = Math.Max(0, movement.QuantityDelta),
                                QuantityReceived = Math.Max(0, movement.QuantityDelta),
                                CostPerItem = movement.UnitCostAtMovement
                            })
                            .ToList()
                    };
                })
                .ToList();

            return planProcurements
                .Concat(legacyProcurements)
                .ToList();
        }

        public ProcurementActionResult AddProcurement(ProcurementViewModel procurement)
        {
            if (!HasPurchaseOrderTables())
            {
                return AddProcurementWithoutPurchaseOrders(procurement);
            }

            if (procurement.Items == null || !procurement.Items.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Add at least one procurement line item."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var supplierName = string.IsNullOrWhiteSpace(procurement.SupplierName)
                ? "Unspecified supplier"
                : procurement.SupplierName.Trim();

            var preparedItems = (procurement.Items ?? new List<ProcurementItemViewModel>())
                .Select(item => new
                {
                    Item = item,
                    QuantityOrdered = Math.Max(item.QuantityOrdered, item.QuantityReceived)
                })
                .Where(row => row.QuantityOrdered > 0 && !string.IsNullOrWhiteSpace(row.Item.ProductId))
                .ToList();
            if (!preparedItems.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Add at least one line item with a valid product and quantity."
                };
            }

            var productIds = preparedItems
                .Select(row => int.TryParse(row.Item.ProductId, out var parsedId) ? parsedId : 0)
                .Where(productId => productId > 0)
                .Distinct()
                .ToList();
            if (!productIds.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "No valid products were submitted."
                };
            }

            var productLookup = ApplyOwnerFilter(_context.Products)
                .Where(product => productIds.Contains(product.ProductID))
                .ToDictionary(product => product.ProductID, product => product);

            var nowUtc = DateTime.UtcNow;
            var selectedBudgetId = ResolveSelectedBudgetId(ownerUserId.Value, procurement.BudgetId);
            var purchaseOrder = new PurchaseOrder
            {
                OwnerUserID = ownerUserId.Value,
                BudgetID = selectedBudgetId,
                PurchaseOrderNumber = GeneratePurchaseOrderNumber(nowUtc),
                SupplierName = supplierName,
                Status = PurchaseOrderStatusDraft,
                TotalAmount = 0m,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            foreach (var row in preparedItems)
            {
                if (!int.TryParse(row.Item.ProductId, out var productId))
                {
                    continue;
                }

                if (!productLookup.TryGetValue(productId, out var product) ||
                    string.Equals(product.MarketPriceSource, ArchivedMarketPriceSource, StringComparison.Ordinal))
                {
                    continue;
                }

                var unitCost = row.Item.CostPerItem > 0m ? row.Item.CostPerItem : product.CostPrice;
                var lineTotal = Math.Round(
                    row.QuantityOrdered * unitCost,
                    2,
                    MidpointRounding.AwayFromZero);

                purchaseOrder.Lines.Add(new PurchaseOrderLine
                {
                    ProductID = productId,
                    QuantityOrdered = row.QuantityOrdered,
                    QuantityReceived = 0,
                    UnitCost = unitCost,
                    LineTotal = lineTotal,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            }

            if (!purchaseOrder.Lines.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "All selected items are invalid or archived. No purchase order was created."
                };
            }

            purchaseOrder.TotalAmount = CalculateOrderedAmount(purchaseOrder.Lines);
            _context.PurchaseOrders.Add(purchaseOrder);
            _context.SaveChanges();

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = $"Purchase order {purchaseOrder.PurchaseOrderNumber} saved as Draft.",
                PurchaseOrderId = purchaseOrder.PurchaseOrderID
            };
        }

        public ProcurementActionResult ApproveProcurement(int purchaseOrderId)
        {
            if (!HasPurchaseOrderTables())
            {
                return ApproveProcurementWithoutPurchaseOrders(purchaseOrderId);
            }

            if (purchaseOrderId <= 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var query = _context.PurchaseOrders
                .Include(purchaseOrder => purchaseOrder.Lines)
                .Include(purchaseOrder => purchaseOrder.Budget)
                .AsQueryable();
            if (_tenantContext.HasOwnerScope)
            {
                var scopedOwnerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(purchaseOrder => purchaseOrder.OwnerUserID == scopedOwnerUserId);
            }

            var purchaseOrder = query.FirstOrDefault(po => po.PurchaseOrderID == purchaseOrderId);
            if (purchaseOrder == null)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            if (!purchaseOrder.Lines.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Cannot approve a purchase order without line items."
                };
            }

            var normalizedStatus = NormalizePurchaseOrderStatus(purchaseOrder.Status);
            if (string.Equals(normalizedStatus, PurchaseOrderStatusCancelled, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Cancelled purchase orders cannot be approved."
                };
            }

            if (string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = true,
                    Message = $"Purchase order {purchaseOrder.PurchaseOrderNumber} is already {normalizedStatus}.",
                    PurchaseOrderId = purchaseOrder.PurchaseOrderID
                };
            }

            var requestedReservation = CalculateRemainingAmount(purchaseOrder.Lines);
            if (requestedReservation <= 0m)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Nothing to reserve. All line quantities are already received."
                };
            }

            FinancialBudget? budget = null;
            if (purchaseOrder.BudgetID.HasValue)
            {
                budget = _context.FinancialBudgets
                    .AsNoTracking()
                    .FirstOrDefault(candidate =>
                        candidate.BudgetID == purchaseOrder.BudgetID.Value &&
                        candidate.OwnerUserID == ownerUserId.Value);

                if (budget == null)
                {
                    return new ProcurementActionResult
                    {
                        Succeeded = false,
                        Message = "Linked budget was not found for this purchase order."
                    };
                }

                var committedAmount = CalculateCommittedAmountForBudget(
                    ownerUserId.Value,
                    budget.BudgetID,
                    excludePurchaseOrderId: purchaseOrder.PurchaseOrderID);
                var availableAmount = Math.Round(
                    budget.BudgetAmount - committedAmount,
                    2,
                    MidpointRounding.AwayFromZero);

                if (requestedReservation > availableAmount)
                {
                    var shortageAmount = Math.Round(
                        requestedReservation - availableAmount,
                        2,
                        MidpointRounding.AwayFromZero);
                    return new ProcurementActionResult
                    {
                        Succeeded = false,
                        Message =
                            $"Budget #{budget.BudgetID} has insufficient available funds. " +
                            $"Available: {availableAmount:C}. Required: {requestedReservation:C}. Shortfall: {shortageAmount:C}.",
                        PurchaseOrderId = purchaseOrder.PurchaseOrderID
                    };
                }
            }

            var nowUtc = DateTime.UtcNow;
            purchaseOrder.Status = PurchaseOrderStatusApproved;
            purchaseOrder.TotalAmount = CalculateOrderedAmount(purchaseOrder.Lines);
            purchaseOrder.ApprovedAtUtc = nowUtc;
            purchaseOrder.UpdatedAtUtc = nowUtc;
            if (budget != null)
            {
                AddBudgetEvent(
                    ownerUserId.Value,
                    budget.BudgetID,
                    BudgetEventTypeReserve,
                    requestedReservation,
                    "Purchase order approved and budget amount reserved.",
                    BudgetEventReferenceTypePurchaseOrder,
                    purchaseOrder.PurchaseOrderNumber,
                    nowUtc);
            }

            _context.SaveChanges();

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = budget != null
                    ? $"Purchase order {purchaseOrder.PurchaseOrderNumber} approved. Linked to budget #{budget.BudgetID}."
                    : $"Purchase order {purchaseOrder.PurchaseOrderNumber} approved without budget approval.",
                PurchaseOrderId = purchaseOrder.PurchaseOrderID
            };
        }

        public ProcurementActionResult DeleteDraftProcurement(int purchaseOrderId)
        {
            if (!HasPurchaseOrderTables())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Draft delete is only available for purchase-order records."
                };
            }

            if (purchaseOrderId <= 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var query = _context.PurchaseOrders
                .Include(purchaseOrder => purchaseOrder.Lines)
                .AsQueryable();
            if (_tenantContext.HasOwnerScope)
            {
                var scopedOwnerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(purchaseOrder => purchaseOrder.OwnerUserID == scopedOwnerUserId);
            }

            var purchaseOrder = query.FirstOrDefault(po => po.PurchaseOrderID == purchaseOrderId);
            if (purchaseOrder == null)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var normalizedStatus = NormalizePurchaseOrderStatus(purchaseOrder.Status);
            if (!string.Equals(normalizedStatus, PurchaseOrderStatusDraft, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = $"Only Draft purchase orders can be deleted. Current status: {normalizedStatus}."
                };
            }

            if (purchaseOrder.Lines.Any(line => line.QuantityReceived > 0))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Cannot delete draft purchase order because some line items were already received."
                };
            }

            var purchaseOrderNumber = purchaseOrder.PurchaseOrderNumber;
            _context.PurchaseOrders.Remove(purchaseOrder);
            _context.SaveChanges();

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = $"Draft purchase order {purchaseOrderNumber} deleted."
            };
        }

        public ProcurementActionResult ReceiveProcurement(int purchaseOrderId, IEnumerable<ProcurementReceiveLineInput>? receiveLines = null)
        {
            if (!HasPurchaseOrderTables())
            {
                return ReceiveProcurementWithoutPurchaseOrders(purchaseOrderId);
            }

            if (purchaseOrderId <= 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var query = _context.PurchaseOrders
                .Include(purchaseOrder => purchaseOrder.Lines)
                .ThenInclude(line => line.Product)
                .AsQueryable();
            if (_tenantContext.HasOwnerScope)
            {
                var scopedOwnerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(purchaseOrder => purchaseOrder.OwnerUserID == scopedOwnerUserId);
            }

            var purchaseOrder = query.FirstOrDefault(po => po.PurchaseOrderID == purchaseOrderId);
            if (purchaseOrder == null)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var normalizedStatus = NormalizePurchaseOrderStatus(purchaseOrder.Status);
            if (!string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = $"Only Approved purchase orders can be received. Current status: {normalizedStatus}."
                };
            }

            var nowUtc = DateTime.UtcNow;
            var receivedAmount = 0m;
            var receivedLines = 0;
            var hasExplicitReceiveLines = receiveLines != null;
            var requestedReceiveByLineId = (receiveLines ?? Enumerable.Empty<ProcurementReceiveLineInput>())
                .Where(line => line.PurchaseOrderLineId > 0)
                .GroupBy(line => line.PurchaseOrderLineId)
                .ToDictionary(
                    group => group.Key,
                    group => Math.Max(0, group.Sum(line => Math.Max(0, line.QuantityToReceive))));

            foreach (var line in purchaseOrder.Lines)
            {
                var quantityRemaining = Math.Max(0, line.QuantityOrdered - line.QuantityReceived);
                if (quantityRemaining <= 0)
                {
                    continue;
                }

                var quantityToReceive = quantityRemaining;
                if (hasExplicitReceiveLines)
                {
                    if (!requestedReceiveByLineId.TryGetValue(line.PurchaseOrderLineID, out var requestedQuantity) ||
                        requestedQuantity <= 0)
                    {
                        continue;
                    }

                    quantityToReceive = Math.Min(quantityRemaining, requestedQuantity);
                    if (quantityToReceive <= 0)
                    {
                        continue;
                    }
                }

                var product = line.Product;
                if (product == null ||
                    string.Equals(product.MarketPriceSource, ArchivedMarketPriceSource, StringComparison.Ordinal))
                {
                    continue;
                }

                var unitCost = line.UnitCost > 0m ? line.UnitCost : product.CostPrice;

                _inventoryControlService.ApplyStockIn(
                    product,
                    quantityToReceive,
                    unitCost,
                    purchaseOrder.SupplierName,
                    $"Purchase order receipt #{purchaseOrder.PurchaseOrderNumber}",
                    ProcurementReferenceType,
                    purchaseOrder.PurchaseOrderNumber,
                    _tenantContext.CurrentUserId);

                line.QuantityReceived += quantityToReceive;
                line.UpdatedAtUtc = nowUtc;

                receivedLines++;
                receivedAmount += quantityToReceive * unitCost;
            }

            if (receivedLines == 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = hasExplicitReceiveLines
                        ? "No line quantities were selected for receipt."
                        : "No receivable line items were processed."
                };
            }

            var remainingAfterReceipt = CalculateRemainingAmount(purchaseOrder.Lines);
            purchaseOrder.TotalAmount = CalculateOrderedAmount(purchaseOrder.Lines);
            purchaseOrder.Status = remainingAfterReceipt > 0m
                ? PurchaseOrderStatusPartiallyReceived
                : PurchaseOrderStatusReceived;
            purchaseOrder.UpdatedAtUtc = nowUtc;
            purchaseOrder.FullyReceivedAtUtc = remainingAfterReceipt > 0m
                ? null
                : nowUtc;
            if (purchaseOrder.BudgetID.HasValue && receivedAmount > 0m)
            {
                AddBudgetEvent(
                    ownerUserId.Value,
                    purchaseOrder.BudgetID.Value,
                    BudgetEventTypeSpend,
                    receivedAmount,
                    "Purchase order receipt recorded against budget.",
                    BudgetEventReferenceTypePurchaseOrder,
                    purchaseOrder.PurchaseOrderNumber,
                    nowUtc);
            }

            _context.SaveChanges();

            var statusMessage = string.Equals(purchaseOrder.Status, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase)
                ? "fully received"
                : "partially received";
            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = $"Purchase order {purchaseOrder.PurchaseOrderNumber} {statusMessage}. Recorded expense: {Math.Round(receivedAmount, 2, MidpointRounding.AwayFromZero):C}." +
                          string.Empty,
                PurchaseOrderId = purchaseOrder.PurchaseOrderID
            };
        }

        public List<SlowMoverSuggestionViewModel> GetSlowMoverSuggestions(int maxItems = 6, int staleDays = 45)
        {
            var normalizedMax = Math.Clamp(maxItems, 1, 20);
            var normalizedStaleDays = Math.Clamp(staleDays, 14, 365);
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;

            var products = ApplyOwnerFilter(_context.Products)
                .AsNoTracking()
                .Where(product =>
                    product.MarketPriceSource != ArchivedMarketPriceSource &&
                    product.StockQuantity > 0)
                .ToList();

            if (!products.Any())
            {
                return new List<SlowMoverSuggestionViewModel>();
            }

            var productIds = products.Select(product => product.ProductID).ToList();
            var saleRows = _context.OrderDetails
                .AsNoTracking()
                .Where(detail => productIds.Contains(detail.ProductID))
                .Where(detail => detail.Order != null && (!applyOwnerFilter || detail.Order!.OwnerUserID == ownerUserId))
                .GroupBy(detail => detail.ProductID)
                .Select(group => new
                {
                    ProductId = group.Key,
                    LastSaleDateUtc = (DateTime?)group.Max(detail => detail.Order!.OrderDate)
                })
                .ToList();

            var lastSaleLookup = saleRows.ToDictionary(row => row.ProductId, row => row.LastSaleDateUtc);

            var today = BusinessTime.Today;

            return products
                .Select(product =>
                {
                    lastSaleLookup.TryGetValue(product.ProductID, out var lastSaleUtc);
                    var localLastSaleDate = lastSaleUtc.HasValue
                        ? ConvertUtcToLocal(lastSaleUtc.Value).Date
                        : (DateTime?)null;
                    var daysSinceLastSale = localLastSaleDate.HasValue
                        ? Math.Max(0, (today - localLastSaleDate.Value).Days)
                        : 9999;

                    var recommendedDiscountPercent = ResolveSlowMoverDiscountPercent(daysSinceLastSale, product.StockQuantity);

                    return new SlowMoverSuggestionViewModel
                    {
                        ProductId = product.ProductID,
                        ProductName = product.ProductName,
                        Sku = product.Sku,
                        Category = NormalizeCategoryName(product.CategoryName),
                        StockQuantity = product.StockQuantity,
                        UnitCost = product.CostPrice,
                        RetailPrice = product.SellingPrice,
                        StockValue = Math.Round(product.StockQuantity * product.CostPrice, 2, MidpointRounding.AwayFromZero),
                        LastSaleDateUtc = lastSaleUtc,
                        DaysSinceLastSale = daysSinceLastSale,
                        RecommendedDiscountPercent = recommendedDiscountPercent
                    };
                })
                .Where(item => item.DaysSinceLastSale >= normalizedStaleDays)
                .OrderByDescending(item => item.DaysSinceLastSale)
                .ThenByDescending(item => item.StockValue)
                .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                .Take(normalizedMax)
                .ToList();
        }

        public InventoryAutomationActionResult QueueSlowMoverPromotion(int productId, int customerLimit = 200)
        {
            if (productId <= 0)
            {
                return new InventoryAutomationActionResult
                {
                    Succeeded = false,
                    Message = "Select a valid product.",
                    AffectedCount = 0
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new InventoryAutomationActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes.",
                    AffectedCount = 0
                };
            }

            var product = ApplyOwnerFilter(_context.Products)
                .FirstOrDefault(candidate =>
                    candidate.ProductID == productId &&
                    candidate.MarketPriceSource != ArchivedMarketPriceSource);

            if (product == null)
            {
                return new InventoryAutomationActionResult
                {
                    Succeeded = false,
                    Message = "Product not found.",
                    AffectedCount = 0
                };
            }

            var normalizedLimit = Math.Clamp(customerLimit, 1, 500);
            var recipientRows = _context.Customers
                .AsNoTracking()
                .Where(customer =>
                    customer.MarketingOptIn &&
                    !string.IsNullOrWhiteSpace(customer.Email) &&
                    customer.Orders.Any(order => order.OwnerUserID == ownerUserId.Value))
                .Select(customer => new
                {
                    customer.Email,
                    customer.FullName,
                    customer.RegistrationDate
                })
                .ToList();

            var recipients = recipientRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Email))
                .GroupBy(row => row.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.RegistrationDate)
                    .First())
                .Take(normalizedLimit)
                .ToList();

            if (!recipients.Any())
            {
                return new InventoryAutomationActionResult
                {
                    Succeeded = false,
                    Message = "No CRM customers with marketing opt-in are eligible for this owner workspace.",
                    AffectedCount = 0
                };
            }

            var encodedProductName = WebUtility.HtmlEncode(product.ProductName);
            var formattedPrice = product.SellingPrice.ToString("C", CultureInfo.GetCultureInfo("en-PH"));
            var subject = $"Inventory Spotlight: {product.ProductName} is available now";

            foreach (var recipient in recipients)
            {
                var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(recipient.FullName) ? "Customer" : recipient.FullName);
                var htmlBody = $@"
                    <h2>Hi {safeName},</h2>
                    <p>We're highlighting <strong>{encodedProductName}</strong> from our current inventory.</p>
                    <p>Current price: <strong>{formattedPrice}</strong></p>
                    <p>Available stock is limited. Reply to this email or visit the store to reserve your unit.</p>
                    <p>Thank you for being part of KSTech.</p>";

                _context.EmailOutbox.Add(new EmailOutbox
                {
                    RecipientEmail = recipient.Email.Trim(),
                    Subject = subject,
                    HtmlBody = htmlBody,
                    OwnerUserID = ownerUserId.Value,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            _context.SaveChanges();

            return new InventoryAutomationActionResult
            {
                Succeeded = true,
                Message = $"Queued slow-mover promo emails for {product.ProductName} to {recipients.Count} CRM customer(s).",
                AffectedCount = recipients.Count
            };
        }

        public InventoryViewModel GetInventoryStats(
            bool showArchived = false,
            string searchTerm = "",
            string category = "All",
            string stockStatus = "All",
            string sortBy = "name_asc",
            int page = 1,
            int pageSize = 10)
        {
            var normalizedSearch = string.IsNullOrWhiteSpace(searchTerm) ? string.Empty : searchTerm.Trim();
            var normalizedCategory = string.IsNullOrWhiteSpace(category)
                ? "All"
                : string.Equals(category.Trim(), "All", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(category.Trim(), "All products", StringComparison.OrdinalIgnoreCase)
                    ? "All"
                    : NormalizeCategoryName(category);
            var normalizedStockStatus = string.IsNullOrWhiteSpace(stockStatus) ? "All" : stockStatus.Trim();
            var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "name_asc" : sortBy.Trim();
            var normalizedPageSize = NormalizePageSize(pageSize);

            var query = ApplyOwnerFilter(_context.Products)
                .Where(product => showArchived
                    ? product.MarketPriceSource == ArchivedMarketPriceSource
                    : product.MarketPriceSource != ArchivedMarketPriceSource)
                .AsQueryable();

            var rawCategoryOptions = query
                .Select(product => product.CategoryName)
                .Where(categoryName => !string.IsNullOrWhiteSpace(categoryName))
                .Distinct()
                .ToList();

            var categoryOptions = rawCategoryOptions
                .Select(NormalizeCategoryName)
                .Where(categoryName => !string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(categoryName => categoryName)
                .ToList();
            var brandOptions = query
                .Where(product => !string.IsNullOrWhiteSpace(product.Brand))
                .Select(product => product.Brand.Trim())
                .Distinct()
                .OrderBy(brand => brand)
                .ToList();

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(product =>
                    product.ProductName.Contains(normalizedSearch) ||
                    product.Sku.Contains(normalizedSearch) ||
                    product.Brand.Contains(normalizedSearch) ||
                    product.CategoryName.Contains(normalizedSearch));
            }

            if (!string.Equals(normalizedCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(product => product.CategoryName == normalizedCategory);
            }

            var products = query.ToList();
            var mappedProducts = products.Select(MapToViewModel);

            if (!string.Equals(normalizedStockStatus, "All", StringComparison.OrdinalIgnoreCase))
            {
                mappedProducts = mappedProducts.Where(product =>
                    string.Equals(product.StockStatus, normalizedStockStatus, StringComparison.OrdinalIgnoreCase));
            }

            var sortedProducts = SortProducts(mappedProducts.ToList(), normalizedSortBy);
            var totalMatched = sortedProducts.Count;
            var totalPages = CalculateTotalPages(totalMatched, normalizedPageSize);
            var normalizedPage = NormalizePage(page, totalPages);
            var pagedProducts = sortedProducts
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return new InventoryViewModel
            {
                ShowArchived = showArchived,
                SearchTerm = normalizedSearch,
                CategoryFilter = normalizedCategory,
                StockStatusFilter = normalizedStockStatus,
                SortBy = normalizedSortBy,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalMatched = totalMatched,
                CategoryOptions = categoryOptions,
                BrandOptions = brandOptions,
                Products = pagedProducts,
                SlowMoverSuggestions = showArchived ? new List<SlowMoverSuggestionViewModel>() : GetSlowMoverSuggestions(),
                TotalStockValue = sortedProducts.Sum(product => product.StockQuantity * product.UnitCost),
                OutOfStockItems = sortedProducts.Count(product => product.StockQuantity == 0),
                LowStockAlerts = sortedProducts.Count(product => product.StockStatus == "Low Stock"),
                StockValueChangePercentage = 0,
                OutOfStockChange = 0
            };
        }

        public InventoryDashboardViewModel GetDashboardStats(DateTime? startDate = null, DateTime? endDate = null)
        {
            // CALC-KPI: Inventory dashboard KPIs are computed from a normalized date window and owner scope.
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            var defaultEndLocalDate = BusinessTime.Today;
            var defaultStartLocalDate = defaultEndLocalDate.AddDays(-29);
            var rangeStartLocalDate = startDate?.Date ?? defaultStartLocalDate;
            var rangeEndLocalDate = endDate?.Date ?? defaultEndLocalDate;
            if (rangeEndLocalDate < rangeStartLocalDate)
            {
                rangeEndLocalDate = rangeStartLocalDate;
            }

            var rangeStart = ConvertLocalDateStartToUtc(rangeStartLocalDate);
            var rangeEnd = ConvertLocalDateEndToUtc(rangeEndLocalDate);
            if (rangeEnd < rangeStart)
            {
                rangeEnd = rangeStart.AddDays(1).AddTicks(-1);
            }
            var rangeDays = Math.Max(1, (int)Math.Ceiling((rangeEnd - rangeStart).TotalDays) + 1);
            var previousRangeEnd = rangeStart.AddTicks(-1);
            var previousRangeStart = previousRangeEnd.Date.AddDays(-rangeDays + 1);

            var products = ApplyOwnerFilter(_context.Products)
                .AsNoTracking()
                .Where(p => p.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList();

            // CALC-KPI: Build an as-of-range-end stock snapshot by reversing stock movements that occurred after the selected end date.
            var postRangeMovements = _context.InventoryMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.OccurredAtUtc > rangeEnd &&
                    (movement.MovementType == "StockIn" || movement.MovementType == "StockOut") &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();

            var postRangeMovementSummaryByProduct = postRangeMovements
                .GroupBy(movement => movement.ProductID)
                .ToDictionary(group => group.Key, SummarizeMovementGroup);

            var inventorySnapshotAtRangeEnd = products
                .Select(product =>
                {
                    postRangeMovementSummaryByProduct.TryGetValue(product.ProductID, out var postRangeSummary);
                    var stockQuantityAtRangeEnd = Math.Max(
                        0,
                        product.StockQuantity - postRangeSummary.StockInUnits + postRangeSummary.StockOutUnits);

                    return new
                    {
                        StockQuantityAtRangeEnd = stockQuantityAtRangeEnd,
                        InventoryValueAtRangeEnd = stockQuantityAtRangeEnd * product.CostPrice,
                        Category = NormalizeCategoryName(product.CategoryName)
                    };
                })
                .ToList();

            // CALC-KPI: Total inventory value uses the selected period's end-of-period stock snapshot.
            decimal totalValue = inventorySnapshotAtRangeEnd.Sum(item => item.InventoryValueAtRangeEnd);

            // CALC-KPI: Low-stock KPI counts products at/under the low-stock threshold (including zero stock).
            //  Low Stock Count & SKUs
            var lowStockItems = products
                .Where(p => CalculateStockStatus(p.StockQuantity, _lowStockThreshold) == "Low Stock" || p.StockQuantity == 0)
                .ToList();
            int lowStockCount = lowStockItems.Count;
            List<string> lowStockSkus = lowStockItems.Select(p => p.Sku).Take(5).ToList();

            // CALC-KPI: Damage KPIs summarize inventory quality risk and potential value loss.
            var damagedItems = products
                .Where(product => product.DamagedQuantity > 0 || IsRiskConditionStatus(product.ConditionStatus))
                .ToList();
            var damagedItemCount = damagedItems.Count;
            var damagedUnitCount = damagedItems.Sum(product => Math.Max(0, product.DamagedQuantity));
            var estimatedDamageLossValue = damagedItems.Sum(product => Math.Max(0, product.DamagedQuantity) * product.CostPrice);
            var totalTrackedUnits = products.Sum(product => Math.Max(0, product.StockQuantity) + Math.Max(0, product.DamagedQuantity));
            var damageRate = totalTrackedUnits > 0
                ? (double)Math.Round((decimal)damagedUnitCount / totalTrackedUnits * 100m, 2, MidpointRounding.AwayFromZero)
                : 0d;
            var damagedSkus = damagedItems
                .Where(product => !string.IsNullOrWhiteSpace(product.Sku))
                .OrderByDescending(product => product.DamagedQuantity)
                .ThenBy(product => product.Sku, StringComparer.OrdinalIgnoreCase)
                .Select(product => product.Sku)
                .Take(5)
                .ToList();

            // CALC-KPI: Stock distribution uses the same end-of-period inventory snapshot as Total Inventory Value.
            var categoryValues = inventorySnapshotAtRangeEnd
                .GroupBy(item => item.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Value = g.Sum(item => item.InventoryValueAtRangeEnd)
                })
                .OrderByDescending(g => g.Value)
                .ToList();
            var totalDistributionValue = categoryValues.Sum(item => item.Value);
            var distribution = categoryValues
                .Select(item => new StockDistributionDataPoint
                {
                    Category = item.Category,
                    Value = item.Value,
                    Percentage = totalDistributionValue > 0m
                        ? (int)Math.Round((item.Value / totalDistributionValue) * 100m, MidpointRounding.AwayFromZero)
                        : 0,
                    Color = GetCategoryColor(item.Category)
                })
                .ToList();

            // CALC-KPI: Sales and sales-change KPIs compare the selected window with an equal-length prior window.
            var currentSales = GetTotalSales(rangeStart, rangeEnd);
            var previousSales = GetTotalSales(previousRangeStart, previousRangeEnd);
            var salesChange = CalculatePercentageChange(currentSales, previousSales);

            // CALC-KPI: Average markup is computed only for products with a non-zero market reference price.
            var markupCandidates = products.Where(p => p.MarketPrice > 0m).ToList();
            var averageMarkup = markupCandidates.Any()
                ? markupCandidates.Average(p => (double)(((p.SellingPrice - p.MarketPrice) / p.MarketPrice) * 100m))
                : 0d;

            var recentMovements = _context.InventoryMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.OccurredAtUtc >= rangeStart &&
                    movement.OccurredAtUtc <= rangeEnd &&
                    (!applyOwnerFilter || movement.OwnerUserID == ownerUserId))
                .ToList();

            // CALC-KPI: Movement deltas estimate unit/value flows and reconstruct start-of-period inventory value.
            var stockInUnitsThisPeriod = recentMovements
                .Where(movement => movement.MovementType == "StockIn")
                .Sum(movement => Math.Max(0, movement.QuantityDelta));
            var stockOutUnitsThisPeriod = recentMovements
                .Where(movement => movement.MovementType == "StockOut")
                .Sum(movement => Math.Abs(movement.QuantityDelta));

            var stockInValueThisPeriod = recentMovements
                .Where(movement => movement.MovementType == "StockIn")
                .Sum(movement => Math.Max(0, movement.QuantityDelta) * movement.UnitCostAtMovement);
            var stockOutValueThisPeriod = recentMovements
                .Where(movement => movement.MovementType == "StockOut")
                .Sum(movement => Math.Abs(movement.QuantityDelta) * movement.UnitCostAtMovement);

            var estimatedStartOfPeriodInventoryValue = totalValue - stockInValueThisPeriod + stockOutValueThisPeriod;
            var inventoryValueChange = CalculatePercentageChange(totalValue, estimatedStartOfPeriodInventoryValue);

            var stockMovementTrend = BuildStockMovementTrend(recentMovements, rangeStart, rangeEnd);

            return new InventoryDashboardViewModel
            {
                FilterStartDate = rangeStartLocalDate,
                FilterEndDate = rangeEndLocalDate,
                TotalInventoryValue = totalValue,
                InventoryValueChange = inventoryValueChange,
                LowStockCount = lowStockCount,
                LowStockSkus = lowStockSkus,
                DamagedItemCount = damagedItemCount,
                DamagedUnitCount = damagedUnitCount,
                EstimatedDamageLossValue = Math.Round(estimatedDamageLossValue, 2, MidpointRounding.AwayFromZero),
                DamageRate = damageRate,
                DamagedSkus = damagedSkus,
                MonthlySales = currentSales,
                MonthlySalesChange = salesChange,
                HighestSalesCategory = CalculateHighestSalesCategory(),
                AvgMarketMarkup = Math.Round(averageMarkup, 2),
                MarkupChange = 0,
                StockDistributionData = distribution,
                StockMovementData = stockMovementTrend,
                StockInUnitsThisMonth = stockInUnitsThisPeriod,
                StockOutUnitsThisMonth = stockOutUnitsThisPeriod
            };
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

        // Helpers
        private ProductViewModel MapToViewModel(Product p)
        {
            var reorderLevel = _lowStockThreshold;
            return new ProductViewModel
            {
                Id = p.ProductID.ToString(),
                Name = p.ProductName,
                Sku = p.Sku,
                Category = NormalizeCategoryName(p.CategoryName),
                Brand = p.Brand,
                Description = p.Description,
                StockQuantity = p.StockQuantity,
                DamagedQuantity = Math.Max(0, p.DamagedQuantity),
                ReorderLevel = reorderLevel,
                UnitCost = p.CostPrice,
                RetailPrice = p.SellingPrice,
                EbayLivePrice = p.MarketPrice,
                MarketPriceSource = p.MarketPriceSource,
                LastMarketPriceSyncUtc = p.LastMarketPriceSyncUtc,
                ImageUrl = p.ImageUrl,
                StockStatus = CalculateStockStatus(p.StockQuantity, reorderLevel),
                ConditionStatus = NormalizeConditionStatus(p.ConditionStatus),
                ConditionNotes = p.ConditionNotes ?? string.Empty,
                LastConditionCheckUtc = p.LastConditionCheckUtc,
                StockPercentage = CalculateStockPercentage(p.StockQuantity, reorderLevel)
            };
        }

        private static string NormalizeConditionStatus(string? rawConditionStatus)
        {
            if (string.IsNullOrWhiteSpace(rawConditionStatus))
            {
                return "Good";
            }

            var normalized = rawConditionStatus.Trim().ToLowerInvariant();
            return normalized switch
            {
                "good" => "Good",
                "damaged" => "Damaged",
                "defective" => "Defective",
                "for inspection" => "For Inspection",
                "forinspection" => "For Inspection",
                "expired" => "Expired",
                _ => "Good"
            };
        }

        private static string NormalizeConditionNotes(string? rawConditionNotes)
        {
            if (string.IsNullOrWhiteSpace(rawConditionNotes))
            {
                return string.Empty;
            }

            var sanitized = rawConditionNotes.Trim();
            if (sanitized.Length <= 300)
            {
                return sanitized;
            }

            return sanitized[..300];
        }

        private static DateTime? NormalizeConditionCheckUtc(DateTime? inputValue, int damagedQuantity, string normalizedConditionStatus)
        {
            if (inputValue.HasValue)
            {
                var value = inputValue.Value;
                return value.Kind switch
                {
                    DateTimeKind.Utc => value,
                    DateTimeKind.Local => value.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
                };
            }

            var hasQualityRisk = Math.Max(0, damagedQuantity) > 0 || IsRiskConditionStatus(normalizedConditionStatus);
            return hasQualityRisk ? DateTime.UtcNow : null;
        }

        private static bool IsRiskConditionStatus(string? conditionStatus)
        {
            var normalized = NormalizeConditionStatus(conditionStatus);
            return string.Equals(normalized, "Damaged", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Defective", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "For Inspection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Expired", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCategoryName(string? rawCategory)
        {
            if (string.IsNullOrWhiteSpace(rawCategory))
            {
                return "Uncategorized";
            }

            var normalized = string.Join(
                ' ',
                rawCategory.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (string.Equals(normalized, "__OTHER__", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "All products", StringComparison.OrdinalIgnoreCase))
            {
                return "Uncategorized";
            }

            return normalized.ToLowerInvariant() switch
            {
                "gpu" => "GPU",
                "cpu" => "CPU",
                "ram" => "RAM",
                "ssd" => "SSD",
                "hdd" => "HDD",
                "psu" => "PSU",
                "motherboard" => "Motherboard",
                "storage" => "Storage",
                "peripherals" => "Peripherals",
                "uncategorized" => "Uncategorized",
                _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant())
            };
        }

        private static List<ProductViewModel> SortProducts(List<ProductViewModel> products, string sortBy)
        {
            return sortBy.ToLowerInvariant() switch
            {
                "name_desc" => products.OrderByDescending(product => product.Name).ToList(),
                "stock_desc" => products.OrderByDescending(product => product.StockQuantity).ToList(),
                "stock_asc" => products.OrderBy(product => product.StockQuantity).ToList(),
                "retail_desc" => products.OrderByDescending(product => product.RetailPrice).ToList(),
                "retail_asc" => products.OrderBy(product => product.RetailPrice).ToList(),
                "market_desc" => products.OrderByDescending(product => product.EbayLivePrice ?? 0m).ToList(),
                "market_asc" => products.OrderBy(product => product.EbayLivePrice ?? 0m).ToList(),
                _ => products.OrderBy(product => product.Name).ToList()
            };
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

        private static List<StockMovementDataPoint> BuildStockMovementTrend(
            List<InventoryMovement> movements,
            DateTime rangeStart,
            DateTime rangeEnd)
        {
            var localStartDate = ConvertUtcToLocal(rangeStart).Date;
            var localEndDate = ConvertUtcToLocal(rangeEnd).Date;
            if (localEndDate < localStartDate)
            {
                localEndDate = localStartDate;
            }

            var durationDays = Math.Max(1, (localEndDate - localStartDate).Days + 1);

            if (durationDays <= 31)
            {
                var byDay = movements
                    .GroupBy(movement => ConvertUtcToLocal(movement.OccurredAtUtc).Date)
                    .ToDictionary(group => group.Key, SummarizeMovementGroup);
                var points = new List<StockMovementDataPoint>();
                for (var day = localStartDate; day <= localEndDate; day = day.AddDays(1))
                {
                    byDay.TryGetValue(day, out var summary);
                    points.Add(new StockMovementDataPoint
                    {
                        Month = day.ToString("MMM dd"),
                        StockInUnits = summary.StockInUnits,
                        StockOutUnits = summary.StockOutUnits
                    });
                }
                return points;
            }

            if (durationDays <= 120)
            {
                var byWeek = movements
                    .GroupBy(movement => GetWeekStart(ConvertUtcToLocal(movement.OccurredAtUtc).Date))
                    .ToDictionary(group => group.Key, SummarizeMovementGroup);
                var points = new List<StockMovementDataPoint>();
                var weekStart = GetWeekStart(localStartDate);
                while (weekStart <= localEndDate)
                {
                    byWeek.TryGetValue(weekStart, out var summary);
                    points.Add(new StockMovementDataPoint
                    {
                        Month = weekStart.ToString("MMM dd"),
                        StockInUnits = summary.StockInUnits,
                        StockOutUnits = summary.StockOutUnits
                    });
                    weekStart = weekStart.AddDays(7);
                }
                return points;
            }

            var byMonth = movements
                .GroupBy(movement =>
                {
                    var localMovementDate = ConvertUtcToLocal(movement.OccurredAtUtc);
                    return new DateTime(localMovementDate.Year, localMovementDate.Month, 1);
                })
                .ToDictionary(group => group.Key, SummarizeMovementGroup);
            var monthlyPoints = new List<StockMovementDataPoint>();
            var monthCursor = new DateTime(localStartDate.Year, localStartDate.Month, 1);
            var monthEnd = new DateTime(localEndDate.Year, localEndDate.Month, 1);
            while (monthCursor <= monthEnd)
            {
                byMonth.TryGetValue(monthCursor, out var summary);
                monthlyPoints.Add(new StockMovementDataPoint
                {
                    Month = monthCursor.ToString("MMM yyyy"),
                    StockInUnits = summary.StockInUnits,
                    StockOutUnits = summary.StockOutUnits
                });
                monthCursor = monthCursor.AddMonths(1);
            }
            return monthlyPoints;
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            var offset = dayOfWeek == 0 ? 6 : dayOfWeek - 1; // Monday-based week
            return date.AddDays(-offset);
        }

        private static (int StockInUnits, int StockOutUnits) SummarizeMovementGroup(IEnumerable<InventoryMovement> movements)
        {
            var stockIn = movements
                .Where(movement => movement.MovementType == "StockIn")
                .Sum(movement => Math.Max(0, movement.QuantityDelta));
            var stockOut = movements
                .Where(movement => movement.MovementType == "StockOut")
                .Sum(movement => Math.Abs(movement.QuantityDelta));
            return (stockIn, stockOut);
        }

        private string CalculateStockStatus(int quantity, int reorderLevel)
        {
            if (quantity == 0) return "Out of Stock";
            if (quantity <= reorderLevel) return "Low Stock";
            if (quantity > reorderLevel * 3) return "Overstock";
            return "Healthy";
        }

        private int CalculateStockPercentage(int quantity, int reorderLevel)
        {
            // CALC-HELPER: Convert stock-to-threshold ratio into a capped 0-100 display percentage.
            if (quantity == 0) return 0;
            if (reorderLevel == 0) return 100;
            double ratio = (double)quantity / (reorderLevel * 4);
            int percentage = (int)(ratio * 100);
            return Math.Min(percentage, 100);
        }

        private string GetCategoryColor(string category)
        {
            return category switch
            {
                "GPU" => "#10B981",
                "CPU" => "#3B82F6",
                "RAM" => "#F59E0B",
                "Storage" => "#EC4899",
                "Motherboard" => "#6366F1",
                _ => "#9CA3AF"
            };
        }
        private decimal CalculateMonthlySales()
        {
            var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            return GetTotalSales(startOfMonth, DateTime.Now);
        }

        private static double CalculatePercentageChange(decimal current, decimal previous)
        {
            // CALC-HELPER: Shared percent-change formula with divide-by-zero handling for inventory KPIs.
            if (previous == 0m)
            {
                return current == 0m ? 0d : 100d;
            }

            return (double)Math.Round(((current - previous) / previous) * 100m, 2);
        }

        public decimal GetTotalSales(DateTime start, DateTime end)
        {
            var query = _context.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end);

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(order => order.OwnerUserID == ownerUserId);
            }

            query = ApplyRecognizedRevenueOrderFilter(query);
            return query.Sum(o => o.TotalAmount);
        }

        private string CalculateHighestSalesCategory()
        {
            var query = _context.OrderDetails
               .Include(od => od.Order)
               .Include(od => od.Product)
               .Where(od => od.Product != null)
               .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(orderDetail => orderDetail.Order != null && orderDetail.Order.OwnerUserID == ownerUserId);
            }

            query = ApplyRecognizedRevenueOrderDetailFilter(query);
            var topCategory = query
               .AsEnumerable()
               .GroupBy(od => NormalizeCategoryName(od.Product!.CategoryName))
               .Select(g => new { Category = g.Key, TotalSales = g.Sum(x => x.SubTotal) })
               .OrderByDescending(x => x.TotalSales)
               .FirstOrDefault();

            return topCategory?.Category ?? "N/A";
        }

        public List<SalesDataPoint> GetSalesHistory(int months)
        {
            var localEndDate = BusinessTime.Today;
            var localStartDate = new DateTime(localEndDate.Year, localEndDate.Month, 1).AddMonths(-months + 1);
            var startUtc = ConvertLocalDateStartToUtc(localStartDate);
            var endUtc = ConvertLocalDateEndToUtc(localEndDate);
            return GetSalesChartData(startUtc, endUtc);
        }

        public List<SalesDataPoint> GetSalesChartData(DateTime start, DateTime end)
        {
            var query = _context.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end);

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(order => order.OwnerUserID == ownerUserId);
            }

            query = ApplyRecognizedRevenueOrderFilter(query);
            var data = query.ToList();

            var result = new List<SalesDataPoint>();
            var localStartDate = ConvertUtcToLocal(start).Date;
            var localEndDate = ConvertUtcToLocal(end).Date;
            if (localEndDate < localStartDate)
            {
                localEndDate = localStartDate;
            }

            var duration = localEndDate - localStartDate;

            if (duration.TotalDays <= 31)
            {
                // Daily Grouping
                var grouped = data.GroupBy(o => ConvertUtcToLocal(o.OrderDate).Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(o => o.TotalAmount) })
                    .ToDictionary(x => x.Date, x => x.Total);

                for (var d = localStartDate; d <= localEndDate; d = d.AddDays(1))
                {
                    result.Add(new SalesDataPoint { Month = d.ToString("dd MMM"), Amount = grouped.ContainsKey(d) ? grouped[d] : 0 });
                }
            }
            else
            {
                // Monthly Grouping
                var grouped = data.GroupBy(o =>
                    {
                        var localOrderDate = ConvertUtcToLocal(o.OrderDate);
                        return new { localOrderDate.Year, localOrderDate.Month };
                    })
                    .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(o => o.TotalAmount) })
                    .ToList();

                // Determine step (month by month)
                var current = new DateTime(localStartDate.Year, localStartDate.Month, 1);
                var localEndMonth = new DateTime(localEndDate.Year, localEndDate.Month, 1);
                while (current <= localEndMonth)
                {
                    var match = grouped.FirstOrDefault(g => g.Year == current.Year && g.Month == current.Month);
                    result.Add(new SalesDataPoint { Month = current.ToString("MMM yyyy"), Amount = match?.Total ?? 0 });
                    current = current.AddMonths(1);
                }
            }
            return result;
        }

        public List<decimal> GetSalesByCategory(DateTime start, DateTime end, out List<string> labels)
        {
            var matchedOrdersQuery = _context.Orders
                .Where(o => o.OrderDate >= start && o.OrderDate <= end)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                matchedOrdersQuery = matchedOrdersQuery.Where(order => order.OwnerUserID == ownerUserId);
            }

            matchedOrdersQuery = ApplyRecognizedRevenueOrderFilter(matchedOrdersQuery);
            var matchedOrders = matchedOrdersQuery
                .Select(o => o.OrderID)
                .ToList();

            var categorySales = _context.OrderDetails
                .Include(od => od.Product)
                .Where(od => matchedOrders.Contains(od.OrderID) && od.Product != null)
                .AsEnumerable()
                .GroupBy(od => NormalizeCategoryName(od.Product!.CategoryName))
                .Select(g => new { Category = g.Key, Total = g.Sum(od => od.SubTotal) })
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToList();

            labels = categorySales.Select(x => x.Category).ToList();
            var data = categorySales.Select(x => x.Total).ToList();

            // Calculate percentage? The prompt implies percentage for pie Chart usually, but Amount works too. 
            // Existing `CategoryData` is List<int> (percentage). Let's convert to percentage of total sales in period.
            decimal total = data.Sum();
            if (total == 0) return new List<decimal>();

            // Returning percentages as decimals for accuracy, but ViewModel expects int?
            // ViewModel has `List<int> CategoryData`. Let's stick to int percentage for compatibility currently.
            // Wait, I should change ViewModel to decimal if I want precision, but keeping int for now.
            return data.Select(d => (decimal)((double)d / (double)total * 100)).ToList();
        }

        public List<RecentOrderViewModel> GetRecentOrders(int count, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.Orders
                .Include(o => o.Customer)
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(order => order.OrderDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(order => order.OrderDate <= endDate.Value);
            }

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                query = query.Where(order => order.OwnerUserID == ownerUserId);
            }

            return query
                .OrderByDescending(o => o.OrderDate)
                .Take(count)
                .Select(o => new RecentOrderViewModel
                {
                    OrderId = o.OrderID.ToString(),
                    CustomerName = o.Customer != null
                        ? (!string.IsNullOrWhiteSpace(o.Customer.FullName)
                            ? o.Customer.FullName
                            : "Guest")
                        : "Guest",
                    OrderDate = o.OrderDate,
                    TotalAmount = o.TotalAmount,
                    Status = o.OrderStatus // It was OrderStatus in Order.cs, but Status in View Model
                })
                .ToList();
        }

        public List<ProductViewModel> GetTopSellingProducts(int count, DateTime? startDate = null, DateTime? endDate = null)
        {
            var topProductsQuery = _context.OrderDetails
                .Include(orderDetail => orderDetail.Order)
                .AsQueryable();

            topProductsQuery = topProductsQuery
                .Where(orderDetail => orderDetail.Order != null);

            if (startDate.HasValue)
            {
                topProductsQuery = topProductsQuery
                    .Where(orderDetail => orderDetail.Order!.OrderDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                topProductsQuery = topProductsQuery
                    .Where(orderDetail => orderDetail.Order!.OrderDate <= endDate.Value);
            }

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                topProductsQuery = topProductsQuery
                    .Where(orderDetail => orderDetail.Order != null && orderDetail.Order.OwnerUserID == ownerUserId);
            }

            topProductsQuery = ApplyRecognizedRevenueOrderDetailFilter(topProductsQuery);
            var topProducts = topProductsQuery
                .GroupBy(od => od.ProductID)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalSold = g.Sum(od => od.Quantity)
                })
                .OrderByDescending(x => x.TotalSold)
                .Take(count)
                .ToList();

            var productIds = topProducts.Select(x => x.ProductId).ToList();
            var products = ApplyOwnerFilter(_context.Products)
                .Where(p =>
                    productIds.Contains(p.ProductID) &&
                    p.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList();

            // Map and attach sold count
            var result = new List<ProductViewModel>();
            foreach (var item in topProducts)
            {
                var p = products.FirstOrDefault(x => x.ProductID == item.ProductId);
                if (p != null)
                {
                    var vm = MapToViewModel(p);
                    // We can reuse a property or add a new one for "Sold Count"
                    // For now, let's misuse "StockPercentage" to store sold count? 
                    // No, that's hacky. Let's just return the ViewModel and maybe add a dynamic property or just use it as is.
                    // Actually, the user wants "Content that makes sense". 
                    // I should probably add a property `TotalSold` to ProductViewModel or create a new ViewModel.
                    // To avoid breaking changes in other views, I will add `TotalSold` to ProductViewModel.
                    vm.TotalSold = item.TotalSold;
                    result.Add(vm);
                }
            }
            return result;
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

        private ProcurementActionResult AddProcurementWithoutPurchaseOrders(ProcurementViewModel procurement)
        {
            if (procurement.Items == null || !procurement.Items.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Add at least one procurement line item."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var supplierName = string.IsNullOrWhiteSpace(procurement.SupplierName)
                ? "Unspecified supplier"
                : procurement.SupplierName.Trim();

            var preparedItems = (procurement.Items ?? new List<ProcurementItemViewModel>())
                .Select(item => new
                {
                    Item = item,
                    QuantityOrdered = Math.Max(item.QuantityOrdered, item.QuantityReceived)
                })
                .Where(row => row.QuantityOrdered > 0 && !string.IsNullOrWhiteSpace(row.Item.ProductId))
                .ToList();
            if (!preparedItems.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Add at least one line item with a valid product and quantity."
                };
            }

            var productIds = preparedItems
                .Select(row => int.TryParse(row.Item.ProductId, out var parsedId) ? parsedId : 0)
                .Where(productId => productId > 0)
                .Distinct()
                .ToList();
            if (!productIds.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "No valid products were submitted."
                };
            }

            var productLookup = ApplyOwnerFilter(_context.Products)
                .Where(product => productIds.Contains(product.ProductID))
                .ToDictionary(product => product.ProductID, product => product);

            var nowUtc = DateTime.UtcNow;
            var purchaseOrderNumber = GenerateProcurementReferenceNumber(nowUtc);
            var budgetId = ResolveSelectedBudgetId(ownerUserId.Value, procurement.BudgetId);
            var planReason = BuildProcurementPlanReason(PurchaseOrderStatusDraft, budgetId);
            var planLines = new List<InventoryMovement>();

            foreach (var row in preparedItems)
            {
                if (!int.TryParse(row.Item.ProductId, out var productId))
                {
                    continue;
                }

                if (!productLookup.TryGetValue(productId, out var product) ||
                    string.Equals(product.MarketPriceSource, ArchivedMarketPriceSource, StringComparison.Ordinal))
                {
                    continue;
                }

                var unitCost = row.Item.CostPerItem > 0m ? row.Item.CostPerItem : product.CostPrice;

                planLines.Add(new InventoryMovement
                {
                    ProductID = productId,
                    OwnerUserID = ownerUserId.Value,
                    MovementType = ProcurementPlanMovementType,
                    QuantityDelta = row.QuantityOrdered,
                    QuantityBefore = 0,
                    QuantityAfter = 0,
                    UnitCostAtMovement = unitCost,
                    PartnerName = supplierName,
                    Reason = planReason,
                    ReferenceType = ProcurementReferenceType,
                    ReferenceId = purchaseOrderNumber,
                    PerformedByUserID = _tenantContext.CurrentUserId,
                    OccurredAtUtc = nowUtc
                });
            }

            if (!planLines.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "All selected items are invalid or archived. No purchase order was created."
                };
            }

            _context.InventoryMovements.AddRange(planLines);
            _context.SaveChanges();

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = $"Purchase order {purchaseOrderNumber} saved as Draft.",
                PurchaseOrderId = planLines.Min(line => line.MovementID)
            };
        }

        private ProcurementActionResult ApproveProcurementWithoutPurchaseOrders(int purchaseOrderId)
        {
            if (purchaseOrderId <= 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var planLines = GetProcurementPlanLinesByActionId(purchaseOrderId, ownerUserId.Value);
            if (!planLines.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var referenceId = planLines.First().ReferenceId;
            var metadata = ParseProcurementPlanReason(planLines.First().Reason);
            var normalizedStatus = NormalizePurchaseOrderStatus(metadata.Status);
            if (string.Equals(normalizedStatus, PurchaseOrderStatusCancelled, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Cancelled purchase orders cannot be approved."
                };
            }

            if (string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = true,
                    Message = $"Purchase order {referenceId} is already {normalizedStatus}.",
                    PurchaseOrderId = purchaseOrderId
                };
            }

            FinancialBudget? budget = null;
            if (metadata.BudgetId.HasValue)
            {
                budget = _context.FinancialBudgets
                    .AsNoTracking()
                    .FirstOrDefault(candidate =>
                        candidate.BudgetID == metadata.BudgetId.Value &&
                        candidate.OwnerUserID == ownerUserId.Value);

                if (budget == null)
                {
                    return new ProcurementActionResult
                    {
                        Succeeded = false,
                        Message = "Linked budget was not found for this purchase order.",
                        PurchaseOrderId = purchaseOrderId
                    };
                }
            }

            var requestedReservation = CalculateRemainingAmountForPlanLines(planLines);
            if (requestedReservation <= 0m)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Nothing to reserve. All line quantities are already received."
                };
            }

            if (budget != null)
            {
                var committedAmount = CalculateCommittedAmountForBudget(
                    ownerUserId.Value,
                    budget.BudgetID,
                    excludeReferenceId: referenceId);
                var availableAmount = Math.Round(
                    budget.BudgetAmount - committedAmount,
                    2,
                    MidpointRounding.AwayFromZero);

                if (requestedReservation > availableAmount)
                {
                    var shortageAmount = Math.Round(
                        requestedReservation - availableAmount,
                        2,
                        MidpointRounding.AwayFromZero);
                    return new ProcurementActionResult
                    {
                        Succeeded = false,
                        Message =
                            $"Budget #{budget.BudgetID} has insufficient available funds. " +
                            $"Available: {availableAmount:C}. Required: {requestedReservation:C}. Shortfall: {shortageAmount:C}.",
                        PurchaseOrderId = purchaseOrderId
                    };
                }
            }

            var updatedReason = BuildProcurementPlanReason(
                PurchaseOrderStatusApproved,
                budget?.BudgetID ?? metadata.BudgetId);
            foreach (var line in planLines)
            {
                line.Reason = updatedReason;
            }
            if (budget != null)
            {
                AddBudgetEvent(
                    ownerUserId.Value,
                    budget.BudgetID,
                    BudgetEventTypeReserve,
                    requestedReservation,
                    "Purchase order approved and budget amount reserved.",
                    BudgetEventReferenceTypePurchaseOrder,
                    referenceId,
                    DateTime.UtcNow);
            }

            _context.SaveChanges();

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = budget != null
                    ? $"Purchase order {referenceId} approved. Linked to budget #{budget.BudgetID}."
                    : $"Purchase order {referenceId} approved without budget approval.",
                PurchaseOrderId = purchaseOrderId
            };
        }

        private ProcurementActionResult ReceiveProcurementWithoutPurchaseOrders(int purchaseOrderId)
        {
            if (purchaseOrderId <= 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var ownerUserId = ResolveWriteOwnerUserId();
            if (!ownerUserId.HasValue)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Select an owner workspace with edit permission before making changes."
                };
            }

            var planLines = GetProcurementPlanLinesByActionId(purchaseOrderId, ownerUserId.Value, includeProducts: true);
            if (!planLines.Any())
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "Purchase order not found."
                };
            }

            var referenceId = planLines.First().ReferenceId;
            var metadata = ParseProcurementPlanReason(planLines.First().Reason);
            var normalizedStatus = NormalizePurchaseOrderStatus(metadata.Status);
            if (!string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase))
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = $"Only Approved purchase orders can be received. Current status: {normalizedStatus}."
                };
            }

            var supplierName = planLines
                .Select(line => line.PartnerName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unspecified supplier";
            var receivedAmount = 0m;
            var receivedLines = 0;

            foreach (var line in planLines)
            {
                var orderedQuantity = GetOrderedQuantity(line);
                var alreadyReceivedQuantity = GetReceivedQuantity(line);
                var quantityRemaining = Math.Max(0, orderedQuantity - alreadyReceivedQuantity);
                if (quantityRemaining <= 0)
                {
                    continue;
                }

                var product = line.Product;
                if (product == null ||
                    string.Equals(product.MarketPriceSource, ArchivedMarketPriceSource, StringComparison.Ordinal))
                {
                    continue;
                }

                var unitCost = line.UnitCostAtMovement > 0m ? line.UnitCostAtMovement : product.CostPrice;

                _inventoryControlService.ApplyStockIn(
                    product,
                    quantityRemaining,
                    unitCost,
                    supplierName,
                    $"Purchase order receipt #{referenceId}",
                    ProcurementReferenceType,
                    referenceId,
                    _tenantContext.CurrentUserId);

                line.QuantityAfter = alreadyReceivedQuantity + quantityRemaining;
                line.UnitCostAtMovement = unitCost;

                receivedLines++;
                receivedAmount += quantityRemaining * unitCost;
            }

            if (receivedLines == 0)
            {
                return new ProcurementActionResult
                {
                    Succeeded = false,
                    Message = "No receivable line items were processed."
                };
            }

            var remainingAfterReceipt = CalculateRemainingAmountForPlanLines(planLines);
            var nextStatus = remainingAfterReceipt > 0m
                ? PurchaseOrderStatusPartiallyReceived
                : PurchaseOrderStatusReceived;
            var updatedReason = BuildProcurementPlanReason(nextStatus, metadata.BudgetId);

            foreach (var line in planLines)
            {
                line.Reason = updatedReason;
            }
            if (metadata.BudgetId.HasValue && receivedAmount > 0m)
            {
                AddBudgetEvent(
                    ownerUserId.Value,
                    metadata.BudgetId.Value,
                    BudgetEventTypeSpend,
                    receivedAmount,
                    "Purchase order receipt recorded against budget.",
                    BudgetEventReferenceTypePurchaseOrder,
                    referenceId,
                    DateTime.UtcNow);
            }

            _context.SaveChanges();

            var statusMessage = string.Equals(nextStatus, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase)
                ? "fully received"
                : "partially received";

            return new ProcurementActionResult
            {
                Succeeded = true,
                Message = $"Purchase order {referenceId} {statusMessage}. Recorded expense: {Math.Round(receivedAmount, 2, MidpointRounding.AwayFromZero):C}." +
                          string.Empty,
                PurchaseOrderId = purchaseOrderId
            };
        }

        private List<InventoryMovement> GetProcurementPlanLinesByActionId(int purchaseOrderId, int ownerUserId, bool includeProducts = false)
        {
            IQueryable<InventoryMovement> anchorQuery = _context.InventoryMovements;
            if (includeProducts)
            {
                anchorQuery = anchorQuery.Include(movement => movement.Product);
            }

            var anchorLine = anchorQuery.FirstOrDefault(movement =>
                movement.MovementID == purchaseOrderId &&
                movement.OwnerUserID == ownerUserId &&
                movement.ReferenceType == ProcurementReferenceType &&
                movement.MovementType == ProcurementPlanMovementType);
            if (anchorLine == null || string.IsNullOrWhiteSpace(anchorLine.ReferenceId))
            {
                return new List<InventoryMovement>();
            }

            IQueryable<InventoryMovement> linesQuery = _context.InventoryMovements;
            if (includeProducts)
            {
                linesQuery = linesQuery.Include(movement => movement.Product);
            }

            return linesQuery
                .Where(movement =>
                    movement.OwnerUserID == ownerUserId &&
                    movement.ReferenceType == ProcurementReferenceType &&
                    movement.MovementType == ProcurementPlanMovementType &&
                    movement.ReferenceId == anchorLine.ReferenceId)
                .OrderBy(movement => movement.MovementID)
                .ToList();
        }

        private static string BuildProcurementPlanReason(string status, int? budgetId)
        {
            var normalizedStatus = NormalizePurchaseOrderStatus(status);
            var normalizedBudget = budgetId.HasValue && budgetId.Value > 0
                ? budgetId.Value.ToString(CultureInfo.InvariantCulture)
                : "0";
            return $"{ProcurementPlanReasonPrefix}|STATUS={normalizedStatus}|BUDGET={normalizedBudget}";
        }

        private static (string Status, int? BudgetId) ParseProcurementPlanReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (PurchaseOrderStatusDraft, null);
            }

            var normalizedStatus = PurchaseOrderStatusDraft;
            int? budgetId = null;
            var segments = reason.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                if (segment.StartsWith("STATUS=", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedStatus = NormalizePurchaseOrderStatus(segment["STATUS=".Length..]);
                    continue;
                }

                if (segment.StartsWith("BUDGET=", StringComparison.OrdinalIgnoreCase))
                {
                    var rawBudget = segment["BUDGET=".Length..];
                    if (int.TryParse(rawBudget, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBudgetId) &&
                        parsedBudgetId > 0)
                    {
                        budgetId = parsedBudgetId;
                    }
                }
            }

            return (normalizedStatus, budgetId);
        }

        private static int GetOrderedQuantity(InventoryMovement movement)
        {
            return Math.Max(0, movement.QuantityDelta);
        }

        private static int GetReceivedQuantity(InventoryMovement movement)
        {
            var orderedQuantity = GetOrderedQuantity(movement);
            return Math.Clamp(movement.QuantityAfter, 0, orderedQuantity);
        }

        private static decimal CalculateOrderedAmountForPlanLines(IEnumerable<InventoryMovement> lines)
        {
            return Math.Round(
                lines.Sum(line => GetOrderedQuantity(line) * line.UnitCostAtMovement),
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateActualExpenseAmountForPlanLines(IEnumerable<InventoryMovement> lines)
        {
            return Math.Round(
                lines.Sum(line => GetReceivedQuantity(line) * line.UnitCostAtMovement),
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateRemainingAmountForPlanLines(IEnumerable<InventoryMovement> lines)
        {
            return Math.Round(
                lines.Sum(line =>
                {
                    var orderedQuantity = GetOrderedQuantity(line);
                    var receivedQuantity = GetReceivedQuantity(line);
                    return Math.Max(0, orderedQuantity - receivedQuantity) * line.UnitCostAtMovement;
                }),
                2,
                MidpointRounding.AwayFromZero);
        }

        private string GenerateProcurementReferenceNumber(DateTime nowUtc)
        {
            var localTimestamp = BusinessTime.ConvertUtcToBusinessTime(nowUtc);
            for (var attempt = 0; attempt < 120; attempt++)
            {
                var candidate = localTimestamp
                    .AddSeconds(attempt)
                    .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var exists = _context.InventoryMovements
                    .AsNoTracking()
                    .Any(movement =>
                        movement.ReferenceType == ProcurementReferenceType &&
                        movement.ReferenceId == candidate);
                if (!exists)
                {
                    return candidate;
                }
            }

            return localTimestamp.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        }

        private decimal CalculateBudgetCommittedAmountFromProcurementPlans(int ownerUserId, int budgetId, string? excludeReferenceId = null)
        {
            var planLines = _context.InventoryMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.OwnerUserID == ownerUserId &&
                    movement.ReferenceType == ProcurementReferenceType &&
                    movement.MovementType == ProcurementPlanMovementType)
                .ToList();

            var committedAmount = 0m;
            foreach (var group in planLines.GroupBy(movement => movement.ReferenceId))
            {
                if (!string.IsNullOrWhiteSpace(excludeReferenceId) &&
                    string.Equals(group.Key, excludeReferenceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var metadata = ParseProcurementPlanReason(group.First().Reason);
                if (!metadata.BudgetId.HasValue || metadata.BudgetId.Value != budgetId)
                {
                    continue;
                }

                var normalizedStatus = NormalizePurchaseOrderStatus(metadata.Status);
                if (string.Equals(normalizedStatus, PurchaseOrderStatusDraft, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedStatus, PurchaseOrderStatusCancelled, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                committedAmount += CalculateActualExpenseAmountForPlanLines(group);
                if (IsReservationStatus(normalizedStatus))
                {
                    committedAmount += CalculateRemainingAmountForPlanLines(group);
                }
            }

            return Math.Round(committedAmount, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateOrderedAmount(IEnumerable<PurchaseOrderLine> lines)
        {
            return Math.Round(
                lines.Sum(line => Math.Max(0, line.QuantityOrdered) * line.UnitCost),
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateActualExpenseAmount(IEnumerable<PurchaseOrderLine> lines)
        {
            return Math.Round(
                lines.Sum(line => Math.Max(0, line.QuantityReceived) * line.UnitCost),
                2,
                MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateRemainingAmount(IEnumerable<PurchaseOrderLine> lines)
        {
            return Math.Round(
                lines.Sum(line => Math.Max(0, line.QuantityOrdered - line.QuantityReceived) * line.UnitCost),
                2,
                MidpointRounding.AwayFromZero);
        }

        private static bool IsReservationStatus(string normalizedStatus)
        {
            return string.Equals(normalizedStatus, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedStatus, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePurchaseOrderStatus(string? status)
        {
            return status?.Trim() switch
            {
                var s when string.Equals(s, PurchaseOrderStatusApproved, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusApproved,
                var s when string.Equals(s, PurchaseOrderStatusPartiallyReceived, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusPartiallyReceived,
                var s when string.Equals(s, PurchaseOrderStatusReceived, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusReceived,
                var s when string.Equals(s, PurchaseOrderStatusCancelled, StringComparison.OrdinalIgnoreCase) => PurchaseOrderStatusCancelled,
                _ => PurchaseOrderStatusDraft
            };
        }

        private string GeneratePurchaseOrderNumber(DateTime nowUtc)
        {
            var localTimestamp = BusinessTime.ConvertUtcToBusinessTime(nowUtc);
            for (var attempt = 0; attempt < 120; attempt++)
            {
                var numericPart = localTimestamp
                    .AddSeconds(attempt)
                    .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var candidate = $"PO-{numericPart}";
                var exists = _context.PurchaseOrders
                    .AsNoTracking()
                    .Any(purchaseOrder => purchaseOrder.PurchaseOrderNumber == candidate);
                if (!exists)
                {
                    return candidate;
                }
            }

            return $"PO-{localTimestamp.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
        }

        private static string ToDisplayProcurementNumber(string? rawNumber)
        {
            if (string.IsNullOrWhiteSpace(rawNumber))
            {
                return string.Empty;
            }

            var normalized = rawNumber.Trim();
            var hasPoPrefix = normalized.StartsWith("PO-", StringComparison.OrdinalIgnoreCase);
            var startIndex = hasPoPrefix ? 3 : 0;
            var digitLength = 0;
            while (startIndex + digitLength < normalized.Length &&
                   char.IsDigit(normalized[startIndex + digitLength]))
            {
                digitLength++;
            }

            // Prefer showing timestamp-like IDs and hide legacy random suffixes.
            if (digitLength >= 14)
            {
                var displayLength = Math.Min(17, digitLength);
                var coreDigits = normalized.Substring(startIndex, displayLength);
                return $"PO-{coreDigits}";
            }

            if (hasPoPrefix)
            {
                return normalized;
            }

            if (normalized.All(char.IsDigit))
            {
                return $"PO-{normalized}";
            }

            return normalized;
        }

        private FinancialBudget? ResolveActiveBudget(int ownerUserId, DateTime localDate)
        {
            return _context.FinancialBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.OwnerUserID == ownerUserId &&
                    budget.Status == "Active" &&
                    budget.PeriodStartDateLocal <= localDate &&
                    budget.PeriodEndDateLocal >= localDate)
                .OrderByDescending(budget => budget.UpdatedAtUtc)
                .ThenByDescending(budget => budget.BudgetID)
                .FirstOrDefault();
        }

        private int? ResolveSelectedBudgetId(int ownerUserId, int? requestedBudgetId)
        {
            if (!requestedBudgetId.HasValue || requestedBudgetId.Value <= 0)
            {
                return null;
            }

            var selectedBudget = _context.FinancialBudgets
                .AsNoTracking()
                .FirstOrDefault(budget =>
                    budget.BudgetID == requestedBudgetId.Value &&
                    budget.OwnerUserID == ownerUserId &&
                    budget.Status == "Active");
            return selectedBudget?.BudgetID;
        }

        private decimal CalculateCommittedAmountForBudget(
            int ownerUserId,
            int budgetId,
            int? excludePurchaseOrderId = null,
            string? excludeReferenceId = null)
        {
            var committedAmount = 0m;

            if (HasPurchaseOrderTables())
            {
                committedAmount += CalculateBudgetCommittedAmount(ownerUserId, budgetId, excludePurchaseOrderId);
            }

            committedAmount += CalculateBudgetCommittedAmountFromProcurementPlans(ownerUserId, budgetId, excludeReferenceId);
            return Math.Round(committedAmount, 2, MidpointRounding.AwayFromZero);
        }

        private decimal CalculateBudgetCommittedAmount(int ownerUserId, int budgetId, int? excludePurchaseOrderId = null)
        {
            var query = _context.PurchaseOrders
                .AsNoTracking()
                .Include(purchaseOrder => purchaseOrder.Lines)
                .Where(purchaseOrder =>
                    purchaseOrder.OwnerUserID == ownerUserId &&
                    purchaseOrder.BudgetID == budgetId)
                .AsQueryable();

            if (excludePurchaseOrderId.HasValue)
            {
                query = query.Where(purchaseOrder => purchaseOrder.PurchaseOrderID != excludePurchaseOrderId.Value);
            }

            var committedAmount = 0m;
            foreach (var purchaseOrder in query.ToList())
            {
                var normalizedStatus = NormalizePurchaseOrderStatus(purchaseOrder.Status);
                if (string.Equals(normalizedStatus, PurchaseOrderStatusDraft, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedStatus, PurchaseOrderStatusCancelled, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                committedAmount += CalculateActualExpenseAmount(purchaseOrder.Lines);
                if (IsReservationStatus(normalizedStatus))
                {
                    committedAmount += CalculateRemainingAmount(purchaseOrder.Lines);
                }
            }

            return Math.Round(committedAmount, 2, MidpointRounding.AwayFromZero);
        }

        private static int ResolveSlowMoverDiscountPercent(int daysSinceLastSale, int stockQuantity)
        {
            if (daysSinceLastSale >= 180 || stockQuantity >= 50)
            {
                return 15;
            }

            if (daysSinceLastSale >= 90 || stockQuantity >= 25)
            {
                return 10;
            }

            return 5;
        }

        private void AddBudgetEvent(
            int ownerUserId,
            int budgetId,
            string eventType,
            decimal amount,
            string reason,
            string referenceType,
            string? referenceId,
            DateTime occurredAtUtc)
        {
            if (budgetId <= 0 || amount <= 0m)
            {
                return;
            }

            _context.BudgetEvents.Add(new BudgetEvent
            {
                OwnerUserID = ownerUserId,
                BudgetID = budgetId,
                EventType = eventType,
                Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
                Reason = string.IsNullOrWhiteSpace(reason) ? "No reason provided." : reason.Trim(),
                ReferenceType = string.IsNullOrWhiteSpace(referenceType) ? BudgetEventReferenceTypePurchaseOrder : referenceType.Trim(),
                ReferenceId = string.IsNullOrWhiteSpace(referenceId) ? string.Empty : referenceId.Trim(),
                PerformedByUserID = _tenantContext.CurrentUserId,
                OccurredAtUtc = occurredAtUtc
            });
        }

        private static IQueryable<Order> ApplyRecognizedRevenueOrderFilter(IQueryable<Order> query)
        {
            return query.Where(order =>
                order.PaymentStatus == "Paid" &&
                order.PaymentStatus != "Refunded" &&
                order.OrderStatus != "Cancelled");
        }

        private static IQueryable<OrderDetail> ApplyRecognizedRevenueOrderDetailFilter(IQueryable<OrderDetail> query)
        {
            return query.Where(orderDetail =>
                orderDetail.Order != null &&
                orderDetail.Order.PaymentStatus == "Paid" &&
                orderDetail.Order.PaymentStatus != "Refunded" &&
                orderDetail.Order.OrderStatus != "Cancelled");
        }

        private IQueryable<Product> ApplyOwnerFilter(IQueryable<Product> query)
        {
            if (!_tenantContext.HasOwnerScope)
            {
                return query;
            }

            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            return query.Where(product => product.OwnerUserID == ownerUserId);
        }

        private int? ResolveWriteOwnerUserId()
        {
            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return null;
            }

            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                return null;
            }

            return ownerUserId;
        }
    }
}
