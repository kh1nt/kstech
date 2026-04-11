using kstech.Configuration;
using kstech.Data;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using kstech.Services;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
const string ExternalScheme = "ExternalScheme";
QuestPDF.Settings.License = LicenseType.Community;
var enforceHttpsOnly = builder.Configuration.GetValue("Security:EnforceHttpsOnly", false);
var cookieSecurePolicy = enforceHttpsOnly
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    dataProtectionKeysPath = Path.Combine(localAppDataPath, "kstech", "dataprotection-keys");
}

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("kstech");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "KSTech.Session";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Custom Authentication (Cookie-based)
// Custom Authentication (Cookie-based - Split Schemes)
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "AdminScheme";
    options.DefaultChallengeScheme = "AdminScheme";
})
    .AddCookie("AdminScheme", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "KSTech.AdminAuth";
    })
    .AddCookie("CustomerScheme", options =>
    {
        options.LoginPath = "/Store/Login";
        options.AccessDeniedPath = "/Store/AccessDenied"; // Need to ensure this exists or maps to Login
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "KSTech.StoreAuth";
    })
    .AddCookie(ExternalScheme, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "KSTech.ExternalAuth";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    });

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = ExternalScheme;
    });
}

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IBusinessIntelligenceService, BusinessIntelligenceService>();
builder.Services.AddTransient<DataSeeder>();
builder.Services.AddHttpContextAccessor(); // Required for AuthService
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection(BrevoOptions.SectionName));
builder.Services.Configure<EbayBrowseOptions>(builder.Configuration.GetSection(EbayBrowseOptions.SectionName));
builder.Services.Configure<SteamOptions>(builder.Configuration.GetSection(SteamOptions.SectionName));
builder.Services.Configure<LoyaltyProgramOptions>(builder.Configuration.GetSection(LoyaltyProgramOptions.SectionName));
builder.Services.Configure<InventoryRuleOptions>(builder.Configuration.GetSection(InventoryRuleOptions.SectionName));
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection(CloudinaryOptions.SectionName));
builder.Services.AddScoped<IEmailOutboxService, EmailOutboxService>();
builder.Services.AddHostedService<kstech.Services.Background.EmailDeliveryWorker>();
// builder.Services.AddHostedService<kstech.Services.Background.MarketPriceSyncWorker>(); // Disabled so API quota isn't exhausted

builder.Services.AddHttpClient<IBrevoEmailService, BrevoEmailService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BrevoOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.HttpTimeoutSeconds, 15, 180));
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BrevoOptions>>().Value;
    var handler = new SocketsHttpHandler
    {
        // Brevo is reachable over IPv4, but IPv6 attempts can stall on some networks.
        // A short connect timeout allows fast fallback to the next resolved address.
        ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.ConnectTimeoutSeconds, 1, 30)),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    handler.ConnectCallback = async (context, cancellationToken) =>
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        Exception? lastException = null;

        // Prefer IPv4 first because some local networks stall on Brevo IPv6 routes.
        foreach (var address in addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                                         .Concat(addresses.Where(address => address.AddressFamily != AddressFamily.InterNetwork)))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                lastException = ex;
            }
        }

        throw lastException ?? new SocketException((int)SocketError.HostNotFound);
    };

    return handler;
});

builder.Services.AddHttpClient<IEbayBrowseService, EbayBrowseService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EbayBrowseOptions>>().Value;
    client.BaseAddress = new Uri(options.BrowseApiBaseUrl);
}).AddStandardResilienceHandler();

builder.Services.AddHttpClient<ISteamService, SteamService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SteamOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
}).AddStandardResilienceHandler();

builder.Services.AddScoped<IMarketPriceSyncService, MarketPriceSyncService>();
builder.Services.AddScoped<IMarketingAutomationService, MarketingAutomationService>();
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();
builder.Services.AddScoped<IInventoryControlService, InventoryControlService>();
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();
builder.Services.AddScoped<IReportCloudArchiveService, ReportCloudArchiveService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (enforceHttpsOnly)
    {
        // Only send HSTS when HTTPS-only mode is enabled.
        app.UseHsts();
    }
}

var defaultCulture = new CultureInfo("en-PH");
CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

var supportedCultures = new[] { defaultCulture.Name };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

// Set Security:EnforceHttpsOnly = true to disable HTTP and force HTTPS-only.
if (enforceHttpsOnly)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Landing}/{action=Index}/{id?}");

// Seeding
var exitAfterStartupTasks = false;
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var seedOptions = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var dataSeeder = services.GetRequiredService<DataSeeder>();

        dataSeeder.EnsureSystemAccounts();

        if (seedOptions.CleanupToSuperAdminOnlyOnStartup)
        {
            logger.LogWarning(
                "Seed:CleanupToSuperAdminOnlyOnStartup is enabled. Automatic sample seeding is skipped while this flag remains true.");
            exitAfterStartupTasks = true;
        }

        var shouldSeed = !seedOptions.CleanupToSuperAdminOnlyOnStartup &&
            seedOptions.EnableAutomaticSeeding &&
            (!seedOptions.EnableInDevelopmentOnly || app.Environment.IsDevelopment());

        if (!shouldSeed)
        {
            if (!seedOptions.CleanupToSuperAdminOnlyOnStartup)
            {
                logger.LogInformation("Automatic seeding is disabled.");
            }
        }
        else
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            dataSeeder.Seed();

            var productCount = context.Products.Count();
            var orderCount = context.Orders.Count();
            logger.LogInformation(
                "Data seeding complete. Database contains {ProductCount} products and {OrderCount} orders.",
                productCount,
                orderCount);
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

if (exitAfterStartupTasks)
{
    return;
}

app.Run();
