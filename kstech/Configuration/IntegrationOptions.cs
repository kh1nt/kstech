namespace kstech.Configuration
{
    public class SeedOptions
    {
        public const string SectionName = "Seed";

        public bool EnableAutomaticSeeding { get; set; }
        public bool EnableInDevelopmentOnly { get; set; } = true;
        public bool EnsureDefaultOwnerAccount { get; set; } = true;
        public bool CleanupToSuperAdminOnlyOnStartup { get; set; }
        public string AdminEmail { get; set; } = "admin@kstech.com";
        public string AdminPassword { get; set; } = string.Empty;
        public string SuperAdminEmail { get; set; } = "superadmin@kstech.com";
        public string SuperAdminPassword { get; set; } = string.Empty;
        public string DefaultCustomerPassword { get; set; } = string.Empty;
    }

    public class BrevoOptions
    {
        public const string SectionName = "Brevo";

        public bool Enabled { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = "KSTech";
        public string BaseUrl { get; set; } = "https://api.brevo.com";
        public int HttpTimeoutSeconds { get; set; } = 60;
        public int ConnectTimeoutSeconds { get; set; } = 3;
    }

    public class EbayBrowseOptions
    {
        public const string SectionName = "Ebay";

        public bool Enabled { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string OAuthUrl { get; set; } = "https://api.ebay.com/identity/v1/oauth2/token";
        public string BrowseApiBaseUrl { get; set; } = "https://api.ebay.com";
        public string Scope { get; set; } = "https://api.ebay.com/oauth/api_scope";
        public string MarketplaceId { get; set; } = "EBAY_US";
        public List<string> StoreUsernames { get; set; } = new();
        public decimal UsdToPhpRate { get; set; } = 56m;
    }

    public class LoyaltyProgramOptions
    {
        public const string SectionName = "LoyaltyProgram";

        public bool Enabled { get; set; } = true;
        public decimal BasePointsPerCurrency { get; set; } = 0.05m; // 1 point per 20 currency
        public decimal PointRedemptionValue { get; set; } = 0.25m; // 1 point = 0.25 currency discount
        public decimal MaxRedemptionRate { get; set; } = 0.20m; // up to 20% of subtotal
        public decimal MinimumOrderAmountForRedemption { get; set; } = 500m;

        public decimal SilverSpendThreshold { get; set; } = 10000m;
        public decimal GoldSpendThreshold { get; set; } = 50000m;
        public decimal PlatinumSpendThreshold { get; set; } = 100000m;

        public decimal BronzeMultiplier { get; set; } = 1.00m;
        public decimal SilverMultiplier { get; set; } = 1.10m;
        public decimal GoldMultiplier { get; set; } = 1.25m;
        public decimal PlatinumMultiplier { get; set; } = 1.50m;
    }

    public class InventoryRuleOptions
    {
        public const string SectionName = "InventoryRules";

        public int LowStockThreshold { get; set; } = 10;
        public int CriticalVarianceUnits { get; set; } = 5;
    }

    public class CloudinaryOptions
    {
        public const string SectionName = "Cloudinary";

        public bool Enabled { get; set; }
        public string CloudName { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string ReportFolder { get; set; } = "kstech/reports";
    }
}
