using kstech.Data;
using Microsoft.EntityFrameworkCore;

namespace kstech.Services
{
    public record MarketPriceSyncResult(int TotalChecked, int Updated, int Failed);

    public interface IMarketPriceSyncService
    {
        Task<MarketPriceSyncResult> SyncProductPricesAsync(int maxProducts = 20, CancellationToken cancellationToken = default);
        Task HandlePriceDropEventAsync(int productId, decimal newPrice);
    }

    public class MarketPriceSyncService : IMarketPriceSyncService
    {
        private const string ArchivedMarketPriceSource = "Archived";
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IEbayBrowseService _ebayBrowseService;
        private readonly IEmailOutboxService _emailOutboxService;
        private readonly ILogger<MarketPriceSyncService> _logger;

        public MarketPriceSyncService(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            IEbayBrowseService ebayBrowseService,
            IEmailOutboxService emailOutboxService,
            ILogger<MarketPriceSyncService> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _ebayBrowseService = ebayBrowseService;
            _emailOutboxService = emailOutboxService;
            _logger = logger;
        }

        public async Task<MarketPriceSyncResult> SyncProductPricesAsync(
            int maxProducts = 20,
            CancellationToken cancellationToken = default)
        {
            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                return new MarketPriceSyncResult(0, 0, 0);
            }

            var normalizedMax = Math.Clamp(maxProducts, 1, 100);
            var productsQuery = _context.Products
                .Where(product => product.MarketPriceSource != ArchivedMarketPriceSource)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                productsQuery = productsQuery.Where(product => product.OwnerUserID == ownerUserId);
            }

            var products = await productsQuery
                .OrderBy(product => product.LastMarketPriceSyncUtc ?? DateTime.MinValue)
                .Take(normalizedMax)
                .ToListAsync(cancellationToken);

            var updated = 0;
            var failed = 0;
            foreach (var product in products)
            {
                try
                {
                    var quote = await _ebayBrowseService.GetMarketPriceAsync(product.ProductName, cancellationToken);

                    if (quote == null || quote.Price < 0)
                    {
                        failed += 1;
                        continue;
                    }

                    product.MarketPrice = quote.Price;
                    product.MarketPriceSource = quote.Source;
                    product.LastMarketPriceSyncUtc = DateTime.UtcNow;
                    updated += 1;
                }
                catch (Exception ex)
                {
                    failed += 1;
                    _logger.LogError(ex, "Failed syncing market price for product {ProductId}.", product.ProductID);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new MarketPriceSyncResult(products.Count, updated, failed);
        }

        public async Task HandlePriceDropEventAsync(int productId, decimal newPrice)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null) return;

            _logger.LogInformation("Cross-API Trigger: Detected major price drop for {ProductName}. Preparing promo email.", product.ProductName);

            // Fetch all customers who opted into marketing (using the Outbox pattern for reliability)
            var optedInCustomers = await _context.Customers
                .Where(c => c.MarketingOptIn)
                .Select(c => c.Email)
                .ToListAsync();

            string subject = $"Price Drop Alert! {product.ProductName} is now {newPrice:C}!";
            string htmlBody = $@"
                <h2>Great news!</h2>
                <p>The market price for <strong>{product.ProductName}</strong> just dropped substantially.</p>
                <p>We currently have {product.StockQuantity} in stock ready to ship at our new competitive price of {newPrice:C}.</p>
                <p>Don't miss out, click here to grab it before stock runs out!</p>
            ";

            foreach (var email in optedInCustomers)
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    await _emailOutboxService.QueueEmailAsync(email, subject, htmlBody, product.OwnerUserID);
                }
            }
        }
    }
}

