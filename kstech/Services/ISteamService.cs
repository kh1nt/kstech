namespace kstech.Services
{
    public record SteamGameMetadata(
        string Name,
        string HeaderImageUrl,
        List<string> Genres,
        string ShortDescription = "",
        string? PcRequirementsMinHtml = null,
        string? PcRequirementsRecHtml = null);

    public record SteamOwnedGame(int AppId, string Name, int PlaytimeForever);
    public record SteamPlayerSummary(string SteamId, string PersonaName, string AvatarUrl);
    public record SteamMostPlayedGame(int Rank, int AppId, int PeakInGame);

    public interface ISteamService
    {
        Task<List<SteamMostPlayedGame>> GetMostPlayedGamesAsync(int count = 9);
        Task<SteamGameMetadata?> GetGameMetadataAsync(int steamAppId);
        Task<(decimal currentPrice, int playerCount)?> GetLiveGameDataAsync(int steamAppId);

        Task<List<SteamOwnedGame>> GetOwnedGamesAsync(string steamId);
        Task<List<int>> GetRecentlyPlayedGamesAsync(string steamId);
        Task<SteamPlayerSummary?> GetPlayerSummariesAsync(string steamId);
        Task<string?> ResolveSteamIdAsync(string input);
    }
}
