namespace kstech.Configuration
{
    public class SteamOptions
    {
        public const string SectionName = "Steam";

        public bool Enabled { get; set; } = false;
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.steampowered.com";
        public string StorefrontBaseUrl { get; set; } = "https://store.steampowered.com";
    }
}
