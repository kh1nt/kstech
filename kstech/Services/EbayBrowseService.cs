using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using kstech.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    public record ExternalPriceQuote(decimal Price, string Currency, string Source, string Reference, List<decimal> TopPrices);

    public interface IEbayBrowseService
    {
        Task<ExternalPriceQuote?> GetMarketPriceAsync(string query, CancellationToken cancellationToken = default);
    }

    public class EbayBrowseService : IEbayBrowseService
    {
        private const string SearchEndpoint = "/buy/browse/v1/item_summary/search";
        private const string FixedPriceFilter = "buyingOptions:{FIXED_PRICE}";
        private const string SearchLimit = "12";
        private readonly HttpClient _httpClient;
        private readonly EbayBrowseOptions _options;
        private readonly ILogger<EbayBrowseService> _logger;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private string _cachedAccessToken = string.Empty;
        private DateTime _accessTokenExpiresUtc = DateTime.MinValue;

        public EbayBrowseService(
            HttpClient httpClient,
            IOptions<EbayBrowseOptions> options,
            ILogger<EbayBrowseService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ExternalPriceQuote?> GetMarketPriceAsync(string query, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var token = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                var storeUsernames = NormalizeStoreUsernames(_options.StoreUsernames);
                if (storeUsernames.Count > 0)
                {
                    var storeQuotes = await SearchMarketQuotesAsync(
                        query,
                        token,
                        storeUsernames,
                        cancellationToken);

                    if (storeQuotes.Count > 0)
                    {
                        var storeMedian = CalculateMedian(storeQuotes);
                        var topStorePrices = storeQuotes.OrderBy(p => Math.Abs(p - storeMedian)).Take(3).ToList();
                        var storeAverage = Math.Round(topStorePrices.Average(), 2);
                        var storeReference = $"{query} | sellers:{string.Join("|", storeUsernames)}";
                        return new ExternalPriceQuote(storeAverage, "PHP", "eBayStore", storeReference, topStorePrices);
                    }
                }

                var browseQuotes = await SearchMarketQuotesAsync(
                    query,
                    token,
                    storeUsernames: null,
                    cancellationToken);

                if (browseQuotes.Count == 0)
                {
                    return null;
                }

                var browseMedian = CalculateMedian(browseQuotes);
                var topPrices = browseQuotes.OrderBy(p => Math.Abs(p - browseMedian)).Take(3).ToList();
                var average = Math.Round(topPrices.Average(), 2);
                return new ExternalPriceQuote(average, "PHP", "eBayBrowse", query, topPrices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "eBay market lookup failed for query '{Query}'.", query);
                return null;
            }
        }

        private async Task<List<decimal>> SearchMarketQuotesAsync(
            string query,
            string token,
            IReadOnlyCollection<string>? storeUsernames,
            CancellationToken cancellationToken)
        {
            var filter = BuildFilter(storeUsernames);
            var url = QueryHelpers.AddQueryString(
                SearchEndpoint,
                new Dictionary<string, string?>
                {
                    ["q"] = query,
                    ["limit"] = SearchLimit,
                    ["filter"] = filter
                });

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", _options.MarketplaceId);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var mode = storeUsernames is { Count: > 0 } ? "store-filtered" : "browse";
                _logger.LogWarning(
                    "eBay {Mode} search failed: {StatusCode} {Body}",
                    mode,
                    response.StatusCode,
                    raw);
                return new List<decimal>();
            }

            return ParseQuotes(raw);
        }

        private List<decimal> ParseQuotes(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("itemSummaries", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return new List<decimal>();
            }

            var quotes = new List<decimal>();
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("price", out var priceNode))
                {
                    continue;
                }

                var value = priceNode.TryGetProperty("value", out var valueNode)
                    ? valueNode.GetString()
                    : null;
                var currency = priceNode.TryGetProperty("currency", out var currencyNode)
                    ? currencyNode.GetString() ?? "USD"
                    : "USD";

                if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    continue;
                }

                var convertedPrice = TryConvertToPhp(parsed, currency);
                if (convertedPrice.HasValue)
                {
                    quotes.Add(convertedPrice.Value);
                }
            }

            return quotes;
        }

        private static decimal CalculateMedian(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0) return 0;
            var sortedList = prices.OrderBy(n => n).ToList();
            var count = sortedList.Count;
            var index = count / 2;
            if (count % 2 == 0)
            {
                return (sortedList[index - 1] + sortedList[index]) / 2.0m;
            }
            return sortedList[index];
        }

        private static List<string> NormalizeStoreUsernames(IEnumerable<string>? usernames)
        {
            if (usernames == null)
            {
                return new List<string>();
            }

            return usernames
                .Where(username => !string.IsNullOrWhiteSpace(username))
                .Select(username => username.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildFilter(IReadOnlyCollection<string>? storeUsernames)
        {
            if (storeUsernames is { Count: > 0 })
            {
                return $"{FixedPriceFilter},sellers:{{{string.Join("|", storeUsernames)}}}";
            }

            return FixedPriceFilter;
        }

        private decimal? TryConvertToPhp(decimal value, string currency)
        {
            if (string.Equals(currency, "PHP", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Round(value, 2);
            }

            if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                var rate = _options.UsdToPhpRate;
                return Math.Round(value * rate, 2);
            }

            _logger.LogDebug("Skipping eBay item price with unsupported currency '{Currency}'.", currency);
            return null;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_cachedAccessToken) &&
                _accessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return _cachedAccessToken;
            }

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedAccessToken) &&
                    _accessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
                {
                    return _cachedAccessToken;
                }

                if (string.IsNullOrWhiteSpace(_options.ClientId) ||
                    string.IsNullOrWhiteSpace(_options.ClientSecret))
                {
                    _logger.LogWarning("eBay credentials missing.");
                    return string.Empty;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthUrl);
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = _options.Scope
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("eBay OAuth failed: {StatusCode} {Body}", response.StatusCode, raw);
                    return string.Empty;
                }

                using var doc = JsonDocument.Parse(raw);
                _cachedAccessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
                var expiresInSeconds = doc.RootElement.GetProperty("expires_in").GetInt32();
                _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);
                return _cachedAccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to obtain eBay OAuth token.");
                return string.Empty;
            }
            finally
            {
                _tokenLock.Release();
            }
        }
    }
}
