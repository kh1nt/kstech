using System.Text.Json;
using kstech.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    public class SteamService : ISteamService
    {
        private readonly HttpClient _httpClient;
        private readonly SteamOptions _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SteamService> _logger;

        public SteamService(HttpClient httpClient, IOptions<SteamOptions> options, IMemoryCache cache, ILogger<SteamService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<SteamMostPlayedGame>> GetMostPlayedGamesAsync(int count = 9)
        {
            if (!_options.Enabled)
            {
                return new List<SteamMostPlayedGame>();
            }

            var normalizedCount = Math.Clamp(count, 1, 100);
            const string cacheKey = "SteamMostPlayedGames";
            if (_cache.TryGetValue(cacheKey, out List<SteamMostPlayedGame>? cachedMostPlayed) &&
                cachedMostPlayed != null &&
                cachedMostPlayed.Count > 0)
            {
                return cachedMostPlayed.Take(normalizedCount).ToList();
            }

            try
            {
                var response = await _httpClient.GetAsync("https://api.steampowered.com/ISteamChartsService/GetMostPlayedGames/v1/");
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                var mostPlayed = new List<SteamMostPlayedGame>();
                if (doc.RootElement.TryGetProperty("response", out var responseEl) &&
                    responseEl.TryGetProperty("ranks", out var ranksEl))
                {
                    foreach (var rankEl in ranksEl.EnumerateArray())
                    {
                        if (!rankEl.TryGetProperty("appid", out var appIdEl) || !appIdEl.TryGetInt32(out var appId))
                        {
                            continue;
                        }

                        var rank = rankEl.TryGetProperty("rank", out var rankValueEl) && rankValueEl.TryGetInt32(out var parsedRank)
                            ? parsedRank
                            : 0;
                        var peakInGame = rankEl.TryGetProperty("peak_in_game", out var peakValueEl) && peakValueEl.TryGetInt32(out var parsedPeak)
                            ? parsedPeak
                            : 0;

                        mostPlayed.Add(new SteamMostPlayedGame(rank, appId, peakInGame));
                    }
                }

                var orderedMostPlayed = mostPlayed
                    .OrderBy(item => item.Rank == 0 ? int.MaxValue : item.Rank)
                    .ToList();

                if (orderedMostPlayed.Count > 0)
                {
                    _cache.Set(
                        cacheKey,
                        orderedMostPlayed,
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                        });
                }

                return orderedMostPlayed.Take(normalizedCount).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching most played games from Steam Charts API.");
                return new List<SteamMostPlayedGame>();
            }
        }

        public async Task<SteamGameMetadata?> GetGameMetadataAsync(int steamAppId)
        {
            if (!_options.Enabled) return null;

            string cacheKey = $"SteamApp_{steamAppId}";
            if (_cache.TryGetValue(cacheKey, out SteamGameMetadata? cachedMetadata))
            {
                return cachedMetadata;
            }

            try
            {
                // Steam's App Details API: https://store.steampowered.com/api/appdetails?appids={appId}
                string url = $"{_options.StorefrontBaseUrl}/api/appdetails?appids={steamAppId}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(jsonStr);
                string appIdStr = steamAppId.ToString();

                if (!doc.RootElement.TryGetProperty(appIdStr, out var appElement) ||
                    !appElement.TryGetProperty("success", out var successElement) ||
                    !successElement.GetBoolean())
                {
                    _logger.LogWarning("Steam API returned false success for AppId {AppId}", steamAppId);
                    return null;
                }

                var dataElement = appElement.GetProperty("data");

                string name = dataElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                string headerImageUrl = dataElement.TryGetProperty("header_image", out var imgEl) ? imgEl.GetString() ?? "" : "";
                string shortDescription = dataElement.TryGetProperty("short_description", out var shortDescEl) ? shortDescEl.GetString() ?? "" : "";

                var genres = new List<string>();
                if (dataElement.TryGetProperty("genres", out var genresArray))
                {
                    foreach (var genre in genresArray.EnumerateArray())
                    {
                        if (genre.TryGetProperty("description", out var desc))
                        {
                            genres.Add(desc.GetString() ?? "");
                        }
                    }
                }

                string? pcReqMin = null;
                string? pcReqRec = null;
                if (dataElement.TryGetProperty("pc_requirements", out var pcReqEl))
                {
                    if (pcReqEl.TryGetProperty("minimum", out var minEl)) pcReqMin = minEl.GetString();
                    if (pcReqEl.TryGetProperty("recommended", out var recEl)) pcReqRec = recEl.GetString();
                }

                var metadata = new SteamGameMetadata(name, headerImageUrl, genres, shortDescription, pcReqMin, pcReqRec);

                // Cache it for a very long time since game names/header images rarely change
                var cacheOptions = new MemoryCacheEntryOptions()
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                };
                _cache.Set(cacheKey, metadata, cacheOptions);

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching metadata from Steam API for AppId {AppId}", steamAppId);
                return null;
            }
        }

        public async Task<(decimal currentPrice, int playerCount)?> GetLiveGameDataAsync(int steamAppId)
        {
            if (!_options.Enabled) return null;
            decimal price = 0;
            int players = 0;

            try
            {
                // Fetch Price from Store API
                string storeUrl = $"{_options.StorefrontBaseUrl}/api/appdetails?appids={steamAppId}&filters=price_overview";
                var storeResponse = await _httpClient.GetAsync(storeUrl);
                if (storeResponse.IsSuccessStatusCode)
                {
                    var storeJson = await storeResponse.Content.ReadAsStringAsync();
                    using var storeDoc = JsonDocument.Parse(storeJson);
                    string appIdStr = steamAppId.ToString();

                    if (storeDoc.RootElement.TryGetProperty(appIdStr, out var appElement) &&
                        appElement.TryGetProperty("success", out var successEl) && successEl.GetBoolean() &&
                        appElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.TryGetProperty("price_overview", out var priceOverviewEl) &&
                        priceOverviewEl.TryGetProperty("final", out var finalEl))
                    {
                        // value is in cents (e.g., 2999 for $29.99), though we treat it generically
                        price = finalEl.GetInt32() / 100m;
                    }
                }

                // Fetch Player Count from Web API
                string playerUrl = $"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={steamAppId}";
                var playerResponse = await _httpClient.GetAsync(playerUrl);
                if (playerResponse.IsSuccessStatusCode)
                {
                    var playerJson = await playerResponse.Content.ReadAsStringAsync();
                    using var playerDoc = JsonDocument.Parse(playerJson);
                    if (playerDoc.RootElement.TryGetProperty("response", out var respEl) &&
                        respEl.TryGetProperty("result", out var resltEl) && resltEl.GetInt32() == 1 &&
                        respEl.TryGetProperty("player_count", out var countEl))
                    {
                        players = countEl.GetInt32();
                    }
                }

                return (price, players);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching live data for AppId {AppId}", steamAppId);
                return null;
            }
        }

        public async Task<List<SteamOwnedGame>> GetOwnedGamesAsync(string steamId)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)) return new List<SteamOwnedGame>();

            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={_options.ApiKey}&steamid={steamId}&include_appinfo=1&include_played_free_games=1&format=json";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new List<SteamOwnedGame>();

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                var ownedGames = new List<SteamOwnedGame>();

                if (doc.RootElement.TryGetProperty("response", out var respEl) &&
                    respEl.TryGetProperty("games", out var gamesEl))
                {
                    foreach (var gameEl in gamesEl.EnumerateArray())
                    {
                        if (gameEl.TryGetProperty("appid", out var appIdEl) &&
                            gameEl.TryGetProperty("name", out var nameEl) &&
                            gameEl.TryGetProperty("playtime_forever", out var playtimeEl))
                        {
                            ownedGames.Add(new SteamOwnedGame(appIdEl.GetInt32(), nameEl.GetString() ?? "", playtimeEl.GetInt32()));
                        }
                    }
                }

                return ownedGames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching owned games for SteamId {SteamId}", steamId);
                return new List<SteamOwnedGame>();
            }
        }

        public async Task<List<int>> GetRecentlyPlayedGamesAsync(string steamId)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)) return new List<int>();

            var url = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key={_options.ApiKey}&steamid={steamId}&format=json";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new List<int>();

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                var recentGames = new List<int>();

                if (doc.RootElement.TryGetProperty("response", out var respEl) &&
                    respEl.TryGetProperty("games", out var gamesEl))
                {
                    foreach (var gameEl in gamesEl.EnumerateArray())
                    {
                        if (gameEl.TryGetProperty("appid", out var appIdEl))
                        {
                            recentGames.Add(appIdEl.GetInt32());
                        }
                    }
                }

                return recentGames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recently played games for SteamId {SteamId}", steamId);
                return new List<int>();
            }
        }

        public async Task<SteamPlayerSummary?> GetPlayerSummariesAsync(string steamId)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)) return null;

            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_options.ApiKey}&steamids={steamId}&format=json";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                if (doc.RootElement.TryGetProperty("response", out var respEl) &&
                    respEl.TryGetProperty("players", out var playersEl) &&
                    playersEl.GetArrayLength() > 0)
                {
                    var playerEl = playersEl[0];
                    if (playerEl.TryGetProperty("steamid", out var sidEl) &&
                        playerEl.TryGetProperty("personaname", out var nameEl) &&
                        playerEl.TryGetProperty("avatarfull", out var avatarEl))
                    {
                        return new SteamPlayerSummary(sidEl.GetString() ?? "", nameEl.GetString() ?? "", avatarEl.GetString() ?? "");
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching player summary for SteamId {SteamId}", steamId);
                return null;
            }
        }

        public async Task<string?> ResolveSteamIdAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            input = input.Trim().TrimEnd('/');

            if (input.Length == 17 && input.StartsWith("7656") && long.TryParse(input, out _))
            {
                return input;
            }

            if (input.Contains("steamcommunity.com/profiles/"))
            {
                var idPart = input.Split("steamcommunity.com/profiles/").Last().Split('/').First();
                if (idPart.Length == 17 && idPart.StartsWith("7656") && long.TryParse(idPart, out _))
                {
                    return idPart;
                }
            }

            string vanityName = input;
            if (input.Contains("steamcommunity.com/id/"))
            {
                vanityName = input.Split("steamcommunity.com/id/").Last().Split('/').First();
            }

            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey)) return null;

            var encodedVanity = Uri.EscapeDataString(vanityName);
            var url = $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/?key={_options.ApiKey}&vanityurl={encodedVanity}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                if (doc.RootElement.TryGetProperty("response", out var respEl) &&
                    respEl.TryGetProperty("success", out var successEl) && successEl.GetInt32() == 1 &&
                    respEl.TryGetProperty("steamid", out var steamIdEl))
                {
                    return steamIdEl.GetString();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving vanity URL {VanityName}", vanityName);
                return null;
            }
        }
    }
}
