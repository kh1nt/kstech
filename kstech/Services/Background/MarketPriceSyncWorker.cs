using kstech.Data;
using Microsoft.EntityFrameworkCore;
using kstech.Services;
using kstech.Models.Entities;

namespace kstech.Services.Background
{
    public class MarketPriceSyncWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MarketPriceSyncWorker> _logger;
        // In real life, this would be an hour or a day. For testing, setting it shorter.
        private readonly TimeSpan _pollingInterval = TimeSpan.FromHours(1);

        public MarketPriceSyncWorker(IServiceProvider serviceProvider, ILogger<MarketPriceSyncWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("eBay Market Price Sync Worker is starting.");

            // Delay initially so we don't hammer eBay the millisecond the app boots
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncPricesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during scheduled eBay price sync.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("eBay Market Price Sync Worker is stopping.");
        }

        private async Task SyncPricesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ebayService = scope.ServiceProvider.GetRequiredService<IEbayBrowseService>();
            var localSyncService = scope.ServiceProvider.GetRequiredService<IMarketPriceSyncService>();

            // Find products that haven't been synced in the last hour
            var productsToSync = await dbContext.Products
                .Where(p => p.MarketPriceSource != "Archived") // Removed SteamAppId filter to allow all products
                .OrderBy(p => p.LastMarketPriceSyncUtc ?? DateTime.MinValue)
                .Take(10) // Small batch
                .ToListAsync(stoppingToken);

            if (!productsToSync.Any()) return;

            foreach (var product in productsToSync)
            {
                var queryTerm = string.IsNullOrWhiteSpace(product.ProductName) ? product.Sku : product.ProductName;
                var quote = await ebayService.GetMarketPriceAsync(queryTerm, cancellationToken: stoppingToken);

                if (quote != null && quote.Price > 0)
                {
                    bool isPriceConfident = ApplyConfidenceRule(product.MarketPrice, quote.Price);

                    if (isPriceConfident)
                    {
                        var oldPrice = product.MarketPrice;
                        product.MarketPrice = quote.Price;
                        product.MarketPriceSource = quote.Source;
                        product.LastMarketPriceSyncUtc = DateTime.UtcNow;

                        _logger.LogInformation(
                            "Sync Success: Product {ProductId} updated from {Old} to {New}. Ref: {Ref}",
                            product.ProductID, oldPrice, quote.Price, quote.Reference);

                        // Trigger the Cross-API logic inside MarketPriceSyncService!
                        if (oldPrice > 0 && quote.Price < oldPrice * 0.9m && product.StockQuantity > 0)
                        {
                            // Price dropped by more than 10% and we have stock!
                            await localSyncService.HandlePriceDropEventAsync(product.ProductID, quote.Price);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Sync Suppressed (Confidence Rule Failed): Product {ProductId}. Current {Current}, Proposed {Proposed}. Ref: {Ref}",
                            product.ProductID, product.MarketPrice, quote.Price, quote.Reference);
                    }
                }
                else
                {
                    _logger.LogWarning("Sync Failed: Could not get a valid quote for Product {ProductId}. Query: {Query}",
                        product.ProductID, queryTerm);
                }

                product.LastMarketPriceSyncUtc = DateTime.UtcNow; // Update even on fail so we don't infinitely retry immediately
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        private bool ApplyConfidenceRule(decimal currentPrice, decimal proposedPrice)
        {
            if (currentPrice <= 0) return true; // Always accept the first price

            // Sane Range Rule: Do not allow an automated price drop/spike of more than 50% in a single sync
            // This prevents wild algorithmic swings from tanking profitability
            var changeRatio = proposedPrice / currentPrice;

            return changeRatio >= 0.5m && changeRatio <= 2.0m;
        }
    }
}
