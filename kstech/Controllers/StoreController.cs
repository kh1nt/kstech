using Microsoft.AspNetCore.Mvc;
using kstech.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using kstech.Models.Entities;
using kstech.Services;
using System.Threading.Tasks;
using kstech.Models.ViewModels;
using System;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using System.Net;

namespace kstech.Controllers
{
    public class StoreController : Controller
    {
        private const string ArchivedMarketPriceSource = "Archived";
        private const string CustomerScheme = "CustomerScheme";
        private const string ExternalScheme = "ExternalScheme";
        private const string CustomerPasswordResetAudience = "Customer";
        private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly IAuthService _authService;
        private readonly ISteamService _steamService;
        private readonly ILogger<StoreController> _logger;
        private readonly IInventoryControlService _inventoryControlService;
        private readonly IEmailOutboxService _emailOutboxService;
        private readonly ILoyaltyService _loyaltyService;
        private readonly IConfiguration _configuration;

        public StoreController(
            kstech.Data.ApplicationDbContext context,
            IAuthService authService,
            ISteamService steamService,
            ILogger<StoreController> logger,
            IInventoryControlService inventoryControlService,
            IEmailOutboxService emailOutboxService,
            ILoyaltyService loyaltyService,
            IConfiguration configuration)
        {
            _context = context;
            _authService = authService;
            _steamService = steamService;
            _logger = logger;
            _inventoryControlService = inventoryControlService;
            _emailOutboxService = emailOutboxService;
            _loyaltyService = loyaltyService;
            _configuration = configuration;
        }

        // Action of Index
        public async Task<IActionResult> Index()
        {
            var products = _context.Products
                .AsNoTracking()
                .Where(product => product.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList();

            var unitsSoldByProduct = GetUnitsSoldByProduct();
            var bestSellerIds = unitsSoldByProduct
                .OrderByDescending(item => item.Value)
                .Take(4)
                .Select(item => item.Key)
                .ToHashSet();

            var mappedProducts = products
                .Select(product => ToProductViewModel(product, unitsSoldByProduct, bestSellerIds))
                .ToList();

            var featuredProducts = mappedProducts
                .OrderByDescending(product => product.FeaturedScore)
                .ThenByDescending(product => product.IsDeal)
                .ThenBy(product => product.Name)
                .Take(6)
                .ToList();

            var trendingGames = await BuildTrendingGamesAsync(4);

            // Recommendations based on Steam Game Library
            var recommendedProducts = new List<ProductViewModel>();
            string recommendationBasis = string.Empty;

            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (currentUserId.HasValue)
            {
                var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.UserID == currentUserId.Value);
                if (customer != null && !string.IsNullOrWhiteSpace(customer.SteamId))
                {
                    var recentGames = await _steamService.GetRecentlyPlayedGamesAsync(customer.SteamId);
                    var ownedGames = await _steamService.GetOwnedGamesAsync(customer.SteamId);

                    var allSteamIds = recentGames.Concat(ownedGames.Select(g => g.AppId)).Distinct().ToList();

                    if (allSteamIds.Any())
                    {
                        var matchingTrendingGames = trendingGames
                            .Where(game => allSteamIds.Contains(game.SteamAppId))
                            .ToList();
                        if (matchingTrendingGames.Any())
                        {
                            recommendedProducts = mappedProducts
                                .OrderByDescending(p => p.FeaturedScore)
                                .Take(4)
                                .ToList();

                            var topGameName = matchingTrendingGames.First().Name;

                            recommendationBasis = $"Upgrading for {topGameName}? Here are our top recommended components.";
                        }
                    }
                }
            }

            var model = new StoreHomeViewModel
            {
                BestSellers = new List<ProductViewModel>(),
                FeaturedProducts = featuredProducts,
                TrendingGames = trendingGames,
                RecommendedProducts = recommendedProducts,
                Categories = products
                    .Select(product => NormalizeCategoryName(product.CategoryName))
                    .Where(categoryName => !string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(categoryName => categoryName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new StoreCategorySummaryViewModel
                    {
                        Name = NormalizeCategoryName(group.Key),
                        Count = group.Count()
                    })
                    .OrderByDescending(category => category.Count)
                    .ThenBy(category => category.Name)
                    .ToList(),
                BestSellerBasis = string.Empty,
                FeaturedBasis = "Ranked using sales demand, current deal value, and stock readiness.",
                RecommendationBasis = recommendationBasis
            };

            return View(model);
        }

        // Action of Products
        public async Task<IActionResult> Products(string category = "All", string brand = "All", string search = "", string sort = "featured", int? steamAppId = null)
        {
            var allProducts = _context.Products
                .AsNoTracking()
                .Where(product => product.MarketPriceSource != ArchivedMarketPriceSource)
                .ToList();

            var selectedCategory = NormalizeCategoryFilter(category);
            var selectedBrand = NormalizeBrandFilter(brand);
            var searchTerm = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim();
            var sortBy = string.IsNullOrWhiteSpace(sort) ? "featured" : sort.Trim().ToLowerInvariant();

            var categories = allProducts
                .Select(product => NormalizeCategoryName(product.CategoryName))
                .Where(categoryName => !string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
                .GroupBy(categoryName => categoryName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StoreCategorySummaryViewModel
                {
                    Name = NormalizeCategoryName(group.Key),
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Name)
                .ToList();

            var brands = allProducts
                .Select(product => NormalizeBrandName(product.Brand))
                .Where(brandName => !string.IsNullOrWhiteSpace(brandName))
                .GroupBy(brandName => brandName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StoreBrandSummaryViewModel
                {
                    Name = NormalizeBrandName(group.Key),
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Name)
                .ToList();

            IEnumerable<Product> filteredProducts = allProducts;
            string? activeGameFilterName = null;
            int? activeGameSteamAppId = null;
            string? activeGamePcRequirementsMinHtml = null;
            string? activeGamePcRequirementsRecHtml = null;

            if (steamAppId.HasValue)
            {
                var metadata = await _steamService.GetGameMetadataAsync(steamAppId.Value);
                if (metadata != null)
                {
                    activeGameFilterName = metadata.Name;
                    activeGameSteamAppId = steamAppId.Value;
                    activeGamePcRequirementsMinHtml = metadata.PcRequirementsMinHtml;
                    activeGamePcRequirementsRecHtml = metadata.PcRequirementsRecHtml;
                }
            }

            if (!string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                filteredProducts = filteredProducts.Where(product =>
                    string.Equals(NormalizeCategoryName(product.CategoryName), selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.Equals(selectedBrand, "All", StringComparison.OrdinalIgnoreCase))
            {
                filteredProducts = filteredProducts.Where(product =>
                    string.Equals(NormalizeBrandName(product.Brand), selectedBrand, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filteredProducts = filteredProducts.Where(product =>
                    (product.ProductName ?? string.Empty).Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (product.Sku ?? string.Empty).Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (product.Brand ?? string.Empty).Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (product.Description ?? string.Empty).Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var unitsSoldByProduct = GetUnitsSoldByProduct();
            var bestSellerIds = unitsSoldByProduct
                .OrderByDescending(item => item.Value)
                .Take(4)
                .Select(item => item.Key)
                .ToHashSet();

            var products = filteredProducts
                .Select(product => ToProductViewModel(product, unitsSoldByProduct, bestSellerIds))
                .ToList();

            products = sortBy switch
            {
                "price_low" => products.OrderBy(product => product.RetailPrice).ThenBy(product => product.Name).ToList(),
                "price_high" => products.OrderByDescending(product => product.RetailPrice).ThenBy(product => product.Name).ToList(),
                "name" => products.OrderBy(product => product.Name).ToList(),
                "stock" => products.OrderByDescending(product => product.StockQuantity).ThenBy(product => product.Name).ToList(),
                _ => products
                    .OrderByDescending(product => product.FeaturedScore)
                    .ThenByDescending(product => product.TotalSold)
                    .ThenBy(product => product.Name)
                    .ToList()
            };

            var model = new StoreCatalogViewModel
            {
                Products = products,
                Categories = categories,
                Brands = brands,
                SelectedCategory = selectedCategory,
                SelectedBrand = selectedBrand,
                SearchTerm = searchTerm,
                SortBy = sortBy,
                ActiveGameFilterName = activeGameFilterName,
                ActiveGameSteamAppId = activeGameSteamAppId,
                ActiveGamePcRequirementsMinHtml = activeGamePcRequirementsMinHtml,
                ActiveGamePcRequirementsRecHtml = activeGamePcRequirementsRecHtml
            };

            return View(model);
        }

        // Action of Details
        public async Task<IActionResult> Details(string id)
        {
            if (!int.TryParse(id, out int productId))
            {
                return NotFound();
            }

            var productEntity = _context.Products
                .FirstOrDefault(p =>
                    p.ProductID == productId &&
                    p.MarketPriceSource != ArchivedMarketPriceSource);

            if (productEntity == null)
            {
                return NotFound();
            }

            var unitsSoldByProduct = GetUnitsSoldByProduct();
            var bestSellerIds = unitsSoldByProduct
                .OrderByDescending(item => item.Value)
                .Take(4)
                .Select(item => item.Key)
                .ToHashSet();

            var product = ToProductViewModel(productEntity, unitsSoldByProduct, bestSellerIds);

            // Product views no longer fetch Steam metadata directly, as products are PC parts.
            // If we want to show anything Steam-related here in the future, it would be based on CompatibleGames.

            return View(product);
        }

        // Action of Games
        public async Task<IActionResult> Games(int page = 1)
        {
            var trendingGames = await BuildTrendingGamesAsync(9);

            var viewModel = new StoreGamesViewModel { Games = trendingGames };

            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (currentUserId.HasValue)
            {
                var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.UserID == currentUserId.Value);
                if (customer != null && !string.IsNullOrWhiteSpace(customer.SteamId))
                {
                    viewModel.IsSteamConnected = true;
                    var recentGames = await _steamService.GetRecentlyPlayedGamesAsync(customer.SteamId);
                    var ownedGames = await _steamService.GetOwnedGamesAsync(customer.SteamId);

                    var ownedGamesSorted = ownedGames.OrderByDescending(g => g.PlaytimeForever).Select(g => g.AppId);
                    
                    var allDistinctSteamIds = recentGames.Concat(ownedGamesSorted).Distinct().ToList();
                    
                    var pagedSteamIds = allDistinctSteamIds.Take(16).ToList();

                    var personalGames = new List<TrendingGameViewModel>();
                    foreach (var appId in pagedSteamIds)
                    {
                        var liveData = await _steamService.GetLiveGameDataAsync(appId);
                        var meta = await _steamService.GetGameMetadataAsync(appId);
                        if (meta != null)
                        {
                            personalGames.Add(new TrendingGameViewModel
                            {
                                SteamAppId = appId,
                                Name = meta.Name,
                                BannerUrl = meta.HeaderImageUrl,
                                PlayerCount = liveData.HasValue ? liveData.Value.playerCount : 0,
                                Genres = meta.Genres ?? new List<string>(),
                                PcRequirementsMinHtml = meta.PcRequirementsMinHtml,
                                PcRequirementsRecHtml = meta.PcRequirementsRecHtml
                            });
                        }
                    }
                    viewModel.PersonalGames = personalGames;
                }
            }

            return View(viewModel);
        }

        // Action of Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // Action of Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthNormal")]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe = false, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Email and password are required.";
                return View();
            }

            var user = await _authService.AuthenticateAsync(email, password);
            if (user == null)
            {
                ViewBag.Error = "Invalid login attempt.";
                return View();
            }

            // Enforce Customer-only login on the store side.
            if (user.Role != "Customer" || user.UserType != "Customer")
            {
                ViewBag.Error = "This login is for customers only. If you are staff, please use the admin login.";
                return View();
            }

            if (!user.IsEmailVerified)
            {
                await QueueCustomerVerificationEmailAsync(user);
                ViewBag.Error = "Please verify your email address before signing in. We sent a verification code to your inbox.";
                return View();
            }

            if (user.TwoFactorEnabled)
            {
                HttpContext.Session.SetString("MfaTemp.Store.UserId", user.UserID.ToString());
                HttpContext.Session.SetString("MfaTemp.Store.RememberMe", rememberMe.ToString());
                HttpContext.Session.SetString("MfaTemp.Store.ReturnUrl", returnUrl ?? "");
                return RedirectToAction(nameof(VerifyMfa));
            }

            await _authService.SignInAsync(user, rememberMe, CustomerScheme);
            await MergeSessionCartIntoCustomerCartAsync(user.UserID);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Store");
        }

        // Action of ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // Action of ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            email = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Email is required.";
                return View();
            }

            try
            {
                await QueueCustomerPasswordResetEmailIfUserExistsAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process customer forgot-password request for {Email}", email);
            }

            TempData["SuccessMessage"] = "If an account exists for that email, a password reset link has been sent.";
            return RedirectToAction(nameof(Login));
        }

        // Action of ResetPassword
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            var resetToken = await GetValidPasswordResetTokenAsync(token, CustomerPasswordResetAudience);
            if (resetToken == null)
            {
                TempData["ErrorMessage"] = "This reset link is invalid or has expired.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            ViewData["Token"] = token.Trim();
            ViewData["Email"] = resetToken.User?.Email;
            return View();
        }

        // Action of ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string token, string newPassword, string confirmPassword)
        {
            ViewData["Token"] = (token ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                ViewBag.Error = "Reset token is missing.";
                return View();
            }

            newPassword = newPassword ?? string.Empty;
            confirmPassword = confirmPassword ?? string.Empty;

            if (string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
            {
                ViewBag.Error = "New password and confirmation are required.";
                return View();
            }

            if (!PasswordPolicy.TryValidate(newPassword, out var passwordPolicyError))
            {
                ViewBag.Error = passwordPolicyError;
                return View();
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                ViewBag.Error = "New password and confirmation do not match.";
                return View();
            }

            var resetToken = await GetValidPasswordResetTokenAsync(token, CustomerPasswordResetAudience);
            if (resetToken?.User == null)
            {
                TempData["ErrorMessage"] = "This reset link is invalid or has expired.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = resetToken.User;
            var now = DateTime.UtcNow;

            user.PasswordHash = _authService.HashPassword(newPassword);
            user.FailedLoginAttempts = 0;
            user.LastFailedLogin = null;
            user.LockoutEnd = null;

            var activeResetTokens = await _context.PasswordResetTokens
                .Where(item =>
                    item.UserID == user.UserID &&
                    item.Audience == CustomerPasswordResetAudience &&
                    item.ConsumedAtUtc == null)
                .ToListAsync();

            foreach (var item in activeResetTokens)
            {
                item.ConsumedAtUtc = now;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your password has been reset successfully. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        // Action of Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _authService.SignOutAsync(CustomerScheme);
            return RedirectToAction("Index");
        }

        // Action of AccessDenied
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return RedirectToAction("Login");
        }

        // Action of Register
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // Action of Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> Register(string fullName, string email, string password, string confirmPassword)
        {
            fullName = (fullName ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            password = password ?? string.Empty;
            confirmPassword = confirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword))
            {
                ViewBag.Error = "All fields are required.";
                return View();
            }

            if (fullName.Length > 50)
            {
                ViewBag.Error = "Full name must be 50 characters or less.";
                return View();
            }

            if (email.Length > 50)
            {
                ViewBag.Error = "Email must be 50 characters or less.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            if (!PasswordPolicy.TryValidate(password, out var passwordPolicyError))
            {
                ViewBag.Error = passwordPolicyError;
                return View();
            }

            var existingUser = await _context.Users.AnyAsync(u => u.Email == email);
            if (existingUser)
            {
                ViewBag.Error = "An account with this email already exists.";
                return View();
            }

            // Create User
            var user = new User
            {
                Email = email,
                PasswordHash = _authService.HashPassword(password),
                FullName = fullName,
                Role = "Customer",
                UserType = "Customer",
                IsActive = true,
                IsEmailVerified = false,
                DateCreated = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Create Customer Record
            var customer = new Customer
            {
                UserID = user.UserID,
                FullName = fullName,
                Email = email,
                Phone = "N/A",
                Address = "",
                City = "",
                RegistrationDate = DateTime.UtcNow
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            await QueueCustomerVerificationEmailAsync(user, forceNewToken: true);
            TempData["SuccessMessage"] = "Account created. Please enter the verification code sent to your email.";
            return RedirectToAction(nameof(VerifyCode), new { email = user.Email });
        }

        // Action of VerifyEmail
        [HttpGet]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            token = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid verification token.";
                return RedirectToAction("Index", "Store");
            }

            var tokenHash = kstech.Services.PasswordResetTokenHelper.HashOtp(token);
            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.IsActive &&
                existing.UserType == "Customer" &&
                existing.Role == "Customer" &&
                existing.EmailVerificationToken == tokenHash);

            if (user == null)
            {
                TempData["ErrorMessage"] = "Verification link has expired or is invalid.";
                return RedirectToAction("Index", "Store");
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your email is already verified.";
                return RedirectToAction("Index", "Store");
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null; // Consume token

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Email verified successfully! You can now complete your checkout.";
            return RedirectToAction("Index", "Store");
        }

        [HttpGet]
        public IActionResult VerifyCode(string email)
        {
            ViewData["Email"] = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> VerifyCode(string email, string code)
        {
            email = (email ?? string.Empty).Trim();
            code = (code ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                ViewBag.Error = "Email and verification code are required.";
                ViewData["Email"] = email;
                return View();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive && u.Role == "Customer" && u.UserType == "Customer");
            var codeHash = kstech.Services.PasswordResetTokenHelper.HashOtp(code);
            var tokenMatch = user != null &&
                !string.IsNullOrEmpty(user.EmailVerificationToken) &&
                kstech.Services.PasswordResetTokenHelper.FixedTimeEqualsOtp(
                    user.EmailVerificationToken, codeHash);

            if (user == null || !tokenMatch)
            {
                ViewBag.Error = "Invalid verification code.";
                ViewData["Email"] = email;
                return View();
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null; // Consume code
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Email verified successfully! You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult VerifyMfa()
        {
            var userIdStr = HttpContext.Session.GetString("MfaTemp.Store.UserId");
            if (string.IsNullOrWhiteSpace(userIdStr))
            {
                return RedirectToAction(nameof(Login));
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> VerifyMfa(string code)
        {
            var userIdStr = HttpContext.Session.GetString("MfaTemp.Store.UserId");
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
            {
                return RedirectToAction(nameof(Login));
            }

            code = (code ?? string.Empty).Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId && u.IsActive);
            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var isMfaVerified = false;
            if (!string.IsNullOrWhiteSpace(user.TwoFactorSecret) && kstech.Utilities.TotpHelper.VerifyCode(user.TwoFactorSecret, code))
            {
                isMfaVerified = true;
            }
            else if (!string.IsNullOrWhiteSpace(user.TwoFactorBackupCodes) && code.Length == 8 && int.TryParse(code, out _))
            {
                var backupCodes = user.TwoFactorBackupCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                var matchedCodeIndex = -1;
                for (int i = 0; i < backupCodes.Count; i++)
                {
                    if (_authService.VerifyPassword(code, backupCodes[i]))
                    {
                        matchedCodeIndex = i;
                        break;
                    }
                }

                if (matchedCodeIndex >= 0)
                {
                    backupCodes.RemoveAt(matchedCodeIndex);
                    user.TwoFactorBackupCodes = backupCodes.Any() ? string.Join(";", backupCodes) : null;
                    await _context.SaveChangesAsync();
                    isMfaVerified = true;
                }
            }

            if (!isMfaVerified)
            {
                ViewBag.Error = "Invalid verification code.";
                return View();
            }

            var rememberMeStr = HttpContext.Session.GetString("MfaTemp.Store.RememberMe");
            bool.TryParse(rememberMeStr, out var rememberMe);
            var returnUrl = HttpContext.Session.GetString("MfaTemp.Store.ReturnUrl");

            // Clean up session
            HttpContext.Session.Remove("MfaTemp.Store.UserId");
            HttpContext.Session.Remove("MfaTemp.Store.RememberMe");
            HttpContext.Session.Remove("MfaTemp.Store.ReturnUrl");

            await _authService.SignInAsync(user, rememberMe, CustomerScheme);
            await MergeSessionCartIntoCustomerCartAsync(user.UserID);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Store");
        }

        // Action of Profile
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.UserID == currentUserId.Value &&
                existing.IsActive &&
                existing.Role == "Customer" &&
                existing.UserType == "Customer");

            if (user == null)
            {
                await _authService.SignOutAsync(CustomerScheme);
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var customer = await EnsureCustomerRecordAsync(user);
            var model = await BuildProfileViewModelAsync(user, customer);
            return View(model);
        }

        // Action of Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(StoreCustomerProfileViewModel model)
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.UserID == currentUserId.Value &&
                existing.IsActive &&
                existing.Role == "Customer" &&
                existing.UserType == "Customer");

            if (user == null)
            {
                await _authService.SignOutAsync(CustomerScheme);
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var customer = await EnsureCustomerRecordAsync(user);

            model.FullName = (model.FullName ?? string.Empty).Trim();
            model.Email = (model.Email ?? string.Empty).Trim();
            model.Phone = (model.Phone ?? string.Empty).Trim();
            model.Address = (model.Address ?? string.Empty).Trim();
            model.City = (model.City ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                await PopulateProfileMetricsAsync(model, customer);
                return View(model);
            }

            var emailInUse = await _context.Users.AnyAsync(existing =>
                existing.UserID != user.UserID &&
                existing.Email == model.Email);

            if (emailInUse)
            {
                ModelState.AddModelError(nameof(model.Email), "This email is already in use by another account.");
                await PopulateProfileMetricsAsync(model, customer);
                return View(model);
            }
            var emailChanged = !string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase);

            user.FullName = model.FullName;
            user.Email = model.Email;
            customer.FullName = model.FullName;
            customer.Email = model.Email;
            customer.Phone = model.Phone;
            customer.Address = model.Address;
            customer.City = model.City;
            customer.MarketingOptIn = model.MarketingOptIn;

            if (emailChanged)
            {
                user.IsEmailVerified = false;
                user.EmailVerificationToken = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(model.SteamId))
            {
                customer.SteamId = null;
            }
            else
            {
                var resolvedId = await _steamService.ResolveSteamIdAsync(model.SteamId);
                if (!string.IsNullOrWhiteSpace(resolvedId))
                {
                    customer.SteamId = resolvedId;
                }
                else
                {
                    ModelState.AddModelError(nameof(model.SteamId), "Could not resolve your Steam ID. Please check the URL or name and try again.");
                    await PopulateProfileMetricsAsync(model, customer);
                    return View(model);
                }
            }

            await _context.SaveChangesAsync();

            if (emailChanged)
            {
                await QueueCustomerVerificationEmailAsync(user);
                await _authService.SignOutAsync(CustomerScheme);
                TempData["SuccessMessage"] = "Profile updated. Verify your new email address before signing in again.";
                return RedirectToAction(nameof(Login));
            }

            TempData["ProfileMessage"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlinkSteam()
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.UserID == currentUserId.Value &&
                existing.IsActive &&
                existing.Role == "Customer" &&
                existing.UserType == "Customer");

            if (user == null)
            {
                await _authService.SignOutAsync(CustomerScheme);
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var customer = await EnsureCustomerRecordAsync(user);
            if (string.IsNullOrWhiteSpace(customer.SteamId))
            {
                TempData["ProfileMessage"] = "Steam account is already unlinked.";
                return RedirectToAction(nameof(Profile));
            }

            customer.SteamId = null;
            await _context.SaveChangesAsync();

            TempData["ProfileMessage"] = "Steam account unlinked successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Authorize(AuthenticationSchemes = CustomerScheme)]
        public async Task<IActionResult> EnableMfa(string secret, string code)
        {
            var userId = await GetCurrentCustomerUserIdAsync();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Store");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Store");

            secret = (secret ?? string.Empty).Trim();
            code = (code ?? string.Empty).Trim();

            if (kstech.Utilities.TotpHelper.VerifyCode(secret, code))
            {
                user.TwoFactorEnabled = true;
                user.TwoFactorSecret = secret;
                await _context.SaveChangesAsync();
                TempData["ProfileMessage"] = "Multi-Factor Authentication enabled successfully.";
            }
            else
            {
                TempData["PasswordError"] = "Invalid verification code. Please try again.";
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Authorize(AuthenticationSchemes = CustomerScheme)]
        public async Task<IActionResult> DisableMfa(string password)
        {
            var userId = await GetCurrentCustomerUserIdAsync();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Store");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Store");

            password = password ?? string.Empty;
            if (_authService.VerifyPassword(password, user.PasswordHash))
            {
                user.TwoFactorEnabled = false;
                user.TwoFactorSecret = null;
                user.TwoFactorBackupCodes = null;
                await _context.SaveChangesAsync();
                TempData["ProfileMessage"] = "Multi-Factor Authentication disabled successfully.";
            }
            else
            {
                TempData["PasswordError"] = "Incorrect password. MFA could not be disabled.";
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> GoogleLogin(string? returnUrl = null)
        {
            var googleClientId = _configuration["Authentication:Google:ClientId"];
            var googleClientSecret = _configuration["Authentication:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
            {
                TempData["ErrorMessage"] = "Google sign-in is not configured yet.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            await HttpContext.SignOutAsync(ExternalScheme);

            var redirectUrl = Url.Action(nameof(GoogleLoginCallback), "Store", new { returnUrl });
            if (string.IsNullOrWhiteSpace(redirectUrl))
            {
                TempData["ErrorMessage"] = "Unable to start Google sign-in.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var authProperties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            return Challenge(authProperties, Microsoft.AspNetCore.Authentication.Google.GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> GoogleLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            if (!string.IsNullOrWhiteSpace(remoteError))
            {
                TempData["ErrorMessage"] = $"Google login failed: {remoteError}";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var externalAuth = await HttpContext.AuthenticateAsync(ExternalScheme);
            if (!externalAuth.Succeeded || externalAuth.Principal == null)
            {
                TempData["ErrorMessage"] = "Google login could not be completed.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var email = externalAuth.Principal.FindFirst(ClaimTypes.Email)?.Value ??
                        externalAuth.Principal.FindFirst("email")?.Value;

            var name = externalAuth.Principal.FindFirst(ClaimTypes.Name)?.Value ?? "Google User";

            await HttpContext.SignOutAsync(ExternalScheme);

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Google account did not provide an email address.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var normalizedEmail = email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive);
            if (user == null)
            {
                // Auto-register customer!
                user = new User
                {
                    Email = normalizedEmail,
                    FullName = name,
                    PasswordHash = _authService.HashPassword(Guid.NewGuid().ToString("N")),
                    Role = "Customer",
                    UserType = "Customer",
                    IsActive = true,
                    IsEmailVerified = true,
                    DateCreated = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var customer = new Customer
                {
                    UserID = user.UserID,
                    FullName = name,
                    Email = normalizedEmail,
                    Phone = "N/A",
                    Address = "",
                    City = "",
                    RegistrationDate = DateTime.UtcNow
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (user.Role != "Customer" || user.UserType != "Customer")
                {
                    TempData["ErrorMessage"] = "This email is registered as staff. Please use password login.";
                    return RedirectToAction(nameof(Login), new { returnUrl });
                }
            }

            if (user.TwoFactorEnabled)
            {
                HttpContext.Session.SetString("MfaTemp.Store.UserId", user.UserID.ToString());
                HttpContext.Session.SetString("MfaTemp.Store.RememberMe", "true");
                HttpContext.Session.SetString("MfaTemp.Store.ReturnUrl", returnUrl ?? "");
                return RedirectToAction(nameof(VerifyMfa));
            }

            await _authService.SignInAsync(user, isPersistent: true, scheme: CustomerScheme);
            await MergeSessionCartIntoCustomerCartAsync(user.UserID);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Store");
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GoogleOneTapLogin(string credential, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                return Json(new { success = false, message = "Credential is required." });
            }

            var googleClientId = _configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(googleClientId))
            {
                return Json(new { success = false, message = "Google authentication is not configured." });
            }

            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={credential}");
                if (!response.IsSuccessStatusCode)
                {
                    return Json(new { success = false, message = "Failed to validate Google credential." });
                }

                var json = await response.Content.ReadAsStringAsync();
                var tokenInfo = System.Text.Json.JsonSerializer.Deserialize<GoogleTokenInfo>(json);

                if (tokenInfo == null || string.IsNullOrWhiteSpace(tokenInfo.Email))
                {
                    return Json(new { success = false, message = "Invalid Google token payload." });
                }

                if (!string.Equals(tokenInfo.Aud, googleClientId, StringComparison.Ordinal))
                {
                    return Json(new { success = false, message = "Audience mismatch." });
                }

                var email = tokenInfo.Email.Trim();
                var name = tokenInfo.Name ?? "Google User";

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
                if (user == null)
                {
                    // Auto-register customer!
                    user = new User
                    {
                        Email = email,
                        FullName = name,
                        PasswordHash = _authService.HashPassword(Guid.NewGuid().ToString("N")),
                        Role = "Customer",
                        UserType = "Customer",
                        IsActive = true,
                        IsEmailVerified = true,
                        DateCreated = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();

                    var customer = new Customer
                    {
                        UserID = user.UserID,
                        FullName = name,
                        Email = email,
                        Phone = "N/A",
                        Address = "",
                        City = "",
                        RegistrationDate = DateTime.UtcNow
                    };
                    _context.Customers.Add(customer);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    if (user.Role != "Customer" || user.UserType != "Customer")
                    {
                        return Json(new { success = false, message = "This account is registered as staff. Please use password login." });
                    }
                }

                if (user.TwoFactorEnabled)
                {
                    HttpContext.Session.SetString("MfaTemp.Store.UserId", user.UserID.ToString());
                    HttpContext.Session.SetString("MfaTemp.Store.RememberMe", "true");
                    HttpContext.Session.SetString("MfaTemp.Store.ReturnUrl", returnUrl ?? "");
                    var mfaUrl = Url.Action(nameof(VerifyMfa), "Store");
                    return Json(new { success = true, redirectUrl = mfaUrl });
                }

                await _authService.SignInAsync(user, isPersistent: true, scheme: CustomerScheme);
                await MergeSessionCartIntoCustomerCartAsync(user.UserID);

                var successRedirectUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : Url.Action("Index", "Store");

                return Json(new { success = true, redirectUrl = successRedirectUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google One Tap login error");
                return Json(new { success = false, message = "Authentication failed." });
            }
        }

        // Action of ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmNewPassword)
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.UserID == currentUserId.Value &&
                existing.IsActive &&
                existing.Role == "Customer" &&
                existing.UserType == "Customer");

            if (user == null)
            {
                await _authService.SignOutAsync(CustomerScheme);
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Profile), "Store") });
            }

            currentPassword = (currentPassword ?? string.Empty).Trim();
            newPassword = (newPassword ?? string.Empty).Trim();
            confirmNewPassword = (confirmNewPassword ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmNewPassword))
            {
                TempData["PasswordError"] = "All password fields are required.";
                return RedirectToAction(nameof(Profile));
            }

            if (!_authService.VerifyPassword(currentPassword, user.PasswordHash))
            {
                TempData["PasswordError"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Profile));
            }

            if (newPassword.Length < 8)
            {
                TempData["PasswordError"] = "New password must be at least 8 characters.";
                return RedirectToAction(nameof(Profile));
            }

            if (!string.Equals(newPassword, confirmNewPassword, StringComparison.Ordinal))
            {
                TempData["PasswordError"] = "New password and confirmation do not match.";
                return RedirectToAction(nameof(Profile));
            }

            if (_authService.VerifyPassword(newPassword, user.PasswordHash))
            {
                TempData["PasswordError"] = "New password must be different from your current password.";
                return RedirectToAction(nameof(Profile));
            }

            user.PasswordHash = _authService.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            TempData["PasswordMessage"] = "Password updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        // Action of Orders
        [HttpGet]
        public async Task<IActionResult> Orders()
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Orders), "Store") });
            }

            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.UserID == currentUserId.Value &&
                existing.IsActive &&
                existing.Role == "Customer" &&
                existing.UserType == "Customer");

            if (user == null)
            {
                await _authService.SignOutAsync(CustomerScheme);
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action(nameof(Orders), "Store") });
            }

            var customer = await EnsureCustomerRecordAsync(user);
            var orders = await _context.Orders
                .AsNoTracking()
                .Where(order => order.CustomerID == customer.CustomerID)
                .Include(order => order.OrderDetails)
                .Include(order => order.Payments)
                .OrderByDescending(order => order.OrderDate)
                .ToListAsync();

            var orderItems = orders.Select(order =>
            {
                var latestPayment = order.Payments
                    .OrderByDescending(payment => payment.PaymentDateUtc)
                    .FirstOrDefault();

                return new StoreOrderHistoryItemViewModel
                {
                    OrderId = order.OrderID,
                    OrderedAtUtc = order.OrderDate,
                    TotalAmount = order.TotalAmount,
                    ItemCount = order.OrderDetails?.Sum(detail => detail.Quantity) ?? 0,
                    OrderStatus = string.IsNullOrWhiteSpace(order.OrderStatus) ? "Unknown" : order.OrderStatus,
                    PaymentStatus = string.IsNullOrWhiteSpace(order.PaymentStatus) ? "Unknown" : order.PaymentStatus,
                    PaymentMethod = latestPayment?.PaymentMethod ?? "N/A",
                    LoyaltyPointsEarned = order.LoyaltyPointsEarned,
                    LoyaltyPointsRedeemed = order.LoyaltyPointsRedeemed
                };
            }).ToList();

            var model = new StoreOrderHistoryViewModel
            {
                Orders = orderItems,
                TotalOrders = orderItems.Count,
                TotalItemsPurchased = orderItems.Sum(item => item.ItemCount),
                LifetimeSpend = orderItems.Sum(item => item.TotalAmount)
            };

            return View(model);
        }

        // Action of CancelOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(AuthenticationSchemes = CustomerScheme)]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == currentUserId.Value);
            if (customer == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderID == id && o.CustomerID == customer.CustomerID);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Order not found or does not belong to you.";
                return RedirectToAction(nameof(Orders));
            }

            if (order.OrderStatus != "Pending" && order.OrderStatus != "Processing")
            {
                TempData["ErrorMessage"] = "Order cannot be cancelled at this stage.";
                return RedirectToAction(nameof(Orders));
            }

            order.OrderStatus = "Cancelled";
            var originalPaymentStatus = order.PaymentStatus ?? "";
            order.PaymentStatus = originalPaymentStatus == "Paid" ? "Refunded" : originalPaymentStatus;

            // Reinstate inventory
            var actorUserId = currentUserId.Value; // Customer cancelling
            foreach (var detail in order.OrderDetails)
            {
                var product = await _context.Products.FindAsync(detail.ProductID);
                if (product != null)
                {
                    _inventoryControlService.ApplyStockIn(
                        product,
                        detail.Quantity,
                        detail.UnitPriceAtSale,
                        "Customer",
                        "Order Cancelled",
                        "Order",
                        order.OrderID.ToString(),
                        actorUserId
                    );
                }
            }

            // Also add a system log
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId,
                OwnerUserID = order.OwnerUserID,
                Action = $"Customer cancelled order #{order.OrderID}",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your order has been cancelled successfully.";
            return RedirectToAction(nameof(Orders));
        }
        private async Task<List<TrendingGameViewModel>> BuildTrendingGamesAsync(int count)
        {
            try
            {
                var mostPlayed = await _steamService.GetMostPlayedGamesAsync(count);
                var trendingGameTasks = mostPlayed.Select(async entry =>
                {
                    var metadata = await _steamService.GetGameMetadataAsync(entry.AppId);
                    return new TrendingGameViewModel
                    {
                        SteamAppId = entry.AppId,
                        Name = !string.IsNullOrWhiteSpace(metadata?.Name) ? metadata.Name : $"Steam App {entry.AppId}",
                        BannerUrl = !string.IsNullOrWhiteSpace(metadata?.HeaderImageUrl)
                            ? metadata.HeaderImageUrl
                            : $"https://cdn.akamai.steamstatic.com/steam/apps/{entry.AppId}/header.jpg",
                        PlayerCount = entry.PeakInGame,
                        Genres = metadata?.Genres ?? new List<string>(),
                        PcRequirementsMinHtml = metadata?.PcRequirementsMinHtml,
                        PcRequirementsRecHtml = metadata?.PcRequirementsRecHtml
                    };
                });

                var trendingGames = await Task.WhenAll(trendingGameTasks);

                return trendingGames
                    .OrderByDescending(game => game.PlayerCount)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build Steam trending games list.");
                return new List<TrendingGameViewModel>();
            }
        }

        private Dictionary<int, int> GetUnitsSoldByProduct()
        {
            // CALC-KPI: Aggregate historical units sold once so product cards can reuse demand metrics.
            return _context.OrderDetails
                .AsNoTracking()
                .GroupBy(item => item.ProductID)
                .Select(group => new
                {
                    ProductId = group.Key,
                    UnitsSold = group.Sum(item => item.Quantity)
                })
                .ToDictionary(item => item.ProductId, item => item.UnitsSold);
        }

        private static ProductViewModel ToProductViewModel(
            Product product,
            IReadOnlyDictionary<int, int> unitsSoldByProduct,
            ISet<int> bestSellerIds)
        {
            var category = NormalizeCategoryName(product.CategoryName);
            var unitsSold = unitsSoldByProduct.TryGetValue(product.ProductID, out var sold) ? sold : 0;
            var hasDeal = product.MarketPrice > product.SellingPrice && product.MarketPrice > 0;
            // CALC-RULE: Deal percentage is measured against market price and shown on the storefront card.
            var discountPercent = hasDeal
                ? Math.Round(((product.MarketPrice - product.SellingPrice) / product.MarketPrice) * 100m, 1)
                : (decimal?)null;
            var featuredScore = CalculateFeaturedScore(product, unitsSold, discountPercent);
            var featuredReason = GetFeaturedReason(product, unitsSold, discountPercent);
            var brand = NormalizeBrandName(product.Brand);
            brand = string.IsNullOrWhiteSpace(brand) ? "N/A" : brand;

            return new ProductViewModel
            {
                Id = product.ProductID.ToString(),
                Name = product.ProductName,
                Sku = product.Sku,
                Category = category,
                Brand = brand,
                Description = string.IsNullOrWhiteSpace(product.Description) ? "No description available." : product.Description,
                StockQuantity = product.StockQuantity,
                StockStatus = GetStockStatus(product.StockQuantity),
                RetailPrice = product.SellingPrice,
                OriginalPrice = hasDeal ? product.MarketPrice : null,
                IsDeal = hasDeal,
                IsBestSeller = bestSellerIds.Contains(product.ProductID),
                DiscountPercent = discountPercent,
                FeaturedScore = featuredScore,
                FeaturedReason = featuredReason,
                ImageUrl = product.ImageUrl ?? string.Empty,
                TotalSold = unitsSold,
                KeySpecs = new Dictionary<string, string>
                {
                    { "Category", category },
                    { "Brand", brand }
                }
            };
        }

        private static string NormalizeCategoryFilter(string? category)
        {
            var normalized = NormalizeDisplayLabel(category);
            if (string.IsNullOrWhiteSpace(normalized) || IsAllCategoryAlias(normalized))
            {
                return "All";
            }

            return NormalizeCategoryName(normalized);
        }

        private static string NormalizeBrandFilter(string? brand)
        {
            var normalized = NormalizeDisplayLabel(brand);
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "All brands", StringComparison.OrdinalIgnoreCase))
            {
                return "All";
            }

            return NormalizeBrandName(normalized);
        }

        private static bool IsAllCategoryAlias(string value)
        {
            return string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "All products", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCategoryName(string? category)
        {
            var normalized = NormalizeDisplayLabel(category);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Uncategorized";
            }

            if (IsAllCategoryAlias(normalized))
            {
                return "All";
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

        private static string NormalizeBrandName(string? brand)
        {
            return NormalizeDisplayLabel(brand);
        }

        private static string NormalizeDisplayLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var tokens = value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', tokens);
        }

        private static double CalculateFeaturedScore(Product product, int unitsSold, decimal? discountPercent)
        {
            // CALC-RULE: Featured score weights demand, stock availability, and discount strength.
            var demandScore = unitsSold * 5d;
            var stockScore = product.StockQuantity <= 0 ? -50d : Math.Min(product.StockQuantity, 25) * 1.2d;
            var discountScore = discountPercent.HasValue ? (double)discountPercent.Value * 2.5d : 0d;
            return demandScore + stockScore + discountScore;
        }

        private static string GetFeaturedReason(Product product, int unitsSold, decimal? discountPercent)
        {
            if (unitsSold > 0 && discountPercent.HasValue)
            {
                return $"Demand + value: {unitsSold} sold and {discountPercent.Value:0.#}% off market.";
            }

            if (unitsSold > 0)
            {
                return $"Demand-driven: {unitsSold} units sold.";
            }

            if (discountPercent.HasValue)
            {
                return $"Value pick: {discountPercent.Value:0.#}% below market price.";
            }

            return product.StockQuantity > 0
                ? "Ready-to-ship stock availability."
                : "Limited now due to stock constraints.";
        }

        private async Task MergeSessionCartIntoCustomerCartAsync(int userId)
        {
            var sessionId = HttpContext.Session.GetString("SessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var sessionCartItems = await _context.CartItems
                .Where(item => item.SessionId == sessionId && item.UserID == null)
                .ToListAsync();

            if (!sessionCartItems.Any())
            {
                return;
            }

            var productIds = sessionCartItems
                .Select(item => item.ProductID)
                .Distinct()
                .ToList();

            var existingUserCartItems = await _context.CartItems
                .Where(item => item.UserID == userId && productIds.Contains(item.ProductID))
                .ToListAsync();

            var stockByProduct = await _context.Products
                .Where(product =>
                    productIds.Contains(product.ProductID) &&
                    product.MarketPriceSource != ArchivedMarketPriceSource)
                .Select(product => new { product.ProductID, product.StockQuantity })
                .ToDictionaryAsync(product => product.ProductID, product => product.StockQuantity);

            foreach (var sessionItem in sessionCartItems)
            {
                if (!stockByProduct.TryGetValue(sessionItem.ProductID, out var stock) || stock <= 0)
                {
                    _context.CartItems.Remove(sessionItem);
                    continue;
                }

                var existingItem = existingUserCartItems.FirstOrDefault(item => item.ProductID == sessionItem.ProductID);
                var cappedQuantity = Math.Min(sessionItem.Quantity, stock);

                if (existingItem == null)
                {
                    sessionItem.UserID = userId;
                    sessionItem.Quantity = cappedQuantity;
                }
                else
                {
                    existingItem.Quantity = Math.Min(existingItem.Quantity + cappedQuantity, stock);
                    _context.CartItems.Remove(sessionItem);
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task<int?> GetCurrentCustomerUserIdAsync()
        {
            var customerAuthResult = await HttpContext.AuthenticateAsync(CustomerScheme);
            if (!customerAuthResult.Succeeded)
            {
                return null;
            }

            var customerIdClaim = customerAuthResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(customerIdClaim, out var userId) ? userId : null;
        }

        // Action of Contact
        [HttpGet]
        [Authorize(AuthenticationSchemes = CustomerScheme)]
        public IActionResult Contact()
        {
            return View(new ContactViewModel());
        }

        // Action of Contact
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(AuthenticationSchemes = CustomerScheme)]
        public async Task<IActionResult> Contact(ContactViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentUserId = await GetCurrentCustomerUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == currentUserId.Value);
            if (customer == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var ownerUserId = await ResolveOwnerUserIdForCustomerInquiryAsync(customer.CustomerID);
            if (!ownerUserId.HasValue)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "We couldn't determine which store workspace should receive your inquiry yet. " +
                    "Please try again after placing an order, or contact support.");
                return View(model);
            }

            var inquiry = new TechnicalInquiry
            {
                OwnerUserID = ownerUserId.Value,
                CustomerID = customer.CustomerID,
                Subject = model.Subject.Trim(),
                InquiryMessage = model.Message.Trim(),
                DateSubmittedUtc = DateTime.UtcNow,
                IsResolved = false
            };

            _context.TechnicalInquiries.Add(inquiry);
            await _context.SaveChangesAsync();

            TempData["ContactSuccess"] = "Your inquiry has been submitted successfully. Our team will get back to you soon.";
            return RedirectToAction(nameof(Contact));
        }

        private async Task<Customer> EnsureCustomerRecordAsync(User user)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(existing => existing.UserID == user.UserID);
            if (customer != null)
            {
                return customer;
            }

            var safeFullName = string.IsNullOrWhiteSpace(user.FullName) ? "Customer" : user.FullName.Trim();
            if (safeFullName.Length > 50)
            {
                safeFullName = safeFullName[..50];
            }

            var safeEmail = string.IsNullOrWhiteSpace(user.Email)
                ? $"customer{user.UserID}@example.com"
                : user.Email.Trim();
            if (safeEmail.Length > 50)
            {
                safeEmail = safeEmail[..50];
            }

            customer = new Customer
            {
                UserID = user.UserID,
                FullName = safeFullName,
                Email = safeEmail,
                Phone = string.Empty,
                Address = string.Empty,
                City = string.Empty,
                RegistrationDate = DateTime.UtcNow,
                MarketingOptIn = true
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
            return customer;
        }

        private async Task<StoreCustomerProfileViewModel> BuildProfileViewModelAsync(User user, Customer customer)
        {
            var secret = user.TwoFactorSecret;
            var qrUrl = "";
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            {
                secret = kstech.Utilities.TotpHelper.GenerateSecret();
                qrUrl = kstech.Utilities.TotpHelper.GenerateOtpAuthUrl(user.Email, "KSTech", secret);
            }
            else if (!user.TwoFactorEnabled)
            {
                qrUrl = kstech.Utilities.TotpHelper.GenerateOtpAuthUrl(user.Email, "KSTech", secret);
            }

            var model = new StoreCustomerProfileViewModel
            {
                FullName = customer.FullName,
                Email = customer.Email,
                Phone = customer.Phone,
                Address = customer.Address,
                City = customer.City,
                MarketingOptIn = customer.MarketingOptIn,
                SteamId = customer.SteamId,
                TwoFactorEnabled = user.TwoFactorEnabled,
                TwoFactorSecret = secret,
                TwoFactorQrUrl = qrUrl
            };

            if (!string.IsNullOrWhiteSpace(customer.SteamId))
            {
                var summary = await _steamService.GetPlayerSummariesAsync(customer.SteamId);
                if (summary != null)
                {
                    model.SteamPersonaName = summary.PersonaName;
                    model.SteamAvatarUrl = summary.AvatarUrl;
                }
            }

            await PopulateProfileMetricsAsync(model, customer);
            return model;
        }

        private async Task PopulateProfileMetricsAsync(StoreCustomerProfileViewModel model, Customer customer)
        {
            var validOrdersQuery = _context.Orders
                .AsNoTracking()
                .Where(order =>
                    order.CustomerID == customer.CustomerID &&
                    order.PaymentStatus != "Refunded");

            model.OrderCount = await validOrdersQuery.CountAsync();
            model.LifetimeSpend = await validOrdersQuery.SumAsync(order => (decimal?)order.TotalAmount) ?? 0m;
            var loyaltyStats = await _context.CustomerTenantLoyalties
                .Where(l => l.CustomerID == customer.CustomerID)
                .Select(l => new { l.LoyaltyPoints, l.LifetimePointsEarned, l.LifetimePointsRedeemed })
                .ToListAsync();

            model.LoyaltyPoints = loyaltyStats.Sum(l => l.LoyaltyPoints);
            model.LifetimePointsEarned = loyaltyStats.Sum(l => l.LifetimePointsEarned);
            model.LifetimePointsRedeemed = loyaltyStats.Sum(l => l.LifetimePointsRedeemed);
            model.CurrentTier = _loyaltyService.ResolveTier(model.LifetimeSpend).Name;
            model.RegistrationDate = customer.RegistrationDate;
        }

        private async Task QueueCustomerPasswordResetEmailIfUserExistsAsync(string email)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(existing =>
                    existing.IsActive &&
                    existing.UserType == "Customer" &&
                    existing.Role == "Customer" &&
                    existing.Email == email);

            if (user == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var activeTokens = await _context.PasswordResetTokens
                .Where(token =>
                    token.UserID == user.UserID &&
                    token.Audience == CustomerPasswordResetAudience &&
                    token.ConsumedAtUtc == null)
                .ToListAsync();

            foreach (var activeToken in activeTokens)
            {
                activeToken.ConsumedAtUtc = now;
            }

            var rawToken = PasswordResetTokenHelper.GenerateRawToken();
            _context.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserID = user.UserID,
                Audience = CustomerPasswordResetAudience,
                TokenHash = PasswordResetTokenHelper.HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(PasswordResetTokenLifetime)
            });

            await _context.SaveChangesAsync();

            var resetLink = Url.Action(nameof(ResetPassword), "Store", new { token = rawToken }, Request.Scheme);
            if (string.IsNullOrWhiteSpace(resetLink))
            {
                _logger.LogWarning("Failed to generate customer password reset link for user {UserId}", user.UserID);
                return;
            }

            var encodedLink = WebUtility.HtmlEncode(resetLink);

            await _emailOutboxService.QueueEmailAsync(
                user.Email,
                "Reset your KSTech Store password",
                $"""
                <p>We received a request to reset your KSTech Store password.</p>
                <p><a href="{encodedLink}">Click here to reset your password</a>.</p>
                <p>This link expires in {(int)PasswordResetTokenLifetime.TotalMinutes} minutes. If you did not request this, you can ignore this email.</p>
                """,
                user.OwnerUserID);
        }

        private async Task QueueCustomerVerificationEmailAsync(User user, bool forceNewToken = false)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            string rawCode;
            if (forceNewToken || string.IsNullOrWhiteSpace(user.EmailVerificationToken))
            {
                rawCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
                user.IsEmailVerified = false;
                user.EmailVerificationToken = kstech.Services.PasswordResetTokenHelper.HashOtp(rawCode);
                await _context.SaveChangesAsync();
            }
            else
            {
                rawCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
                user.EmailVerificationToken = kstech.Services.PasswordResetTokenHelper.HashOtp(rawCode);
                await _context.SaveChangesAsync();
            }

            var verificationEmailOwnerUserId = await ResolveSingleActiveOwnerUserIdAsync();
            var encodedName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(user.FullName) ? "Customer" : user.FullName);
            var encodedCode = WebUtility.HtmlEncode(rawCode);

            await _emailOutboxService.QueueEmailAsync(
                user.Email,
                $"Your KSTech Verification Code: {encodedCode}",
                $"""
                <p>Hi {encodedName},</p>
                <p>Thank you for registering. Please use the following 6-digit verification code to verify your email address:</p>
                <h2 style="font-size: 24px; letter-spacing: 4px; color: #1a5c58; text-align: center; margin: 20px 0; font-family: monospace;">{encodedCode}</h2>
                <p>You must verify your email before placing orders.</p>
                """,
                verificationEmailOwnerUserId);
        }

        private async Task<PasswordResetToken?> GetValidPasswordResetTokenAsync(string? rawToken, string audience)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return null;
            }

            var tokenHash = PasswordResetTokenHelper.HashToken(rawToken);
            var now = DateTime.UtcNow;

            var resetToken = await _context.PasswordResetTokens
                .Include(token => token.User)
                .FirstOrDefaultAsync(token =>
                    token.TokenHash == tokenHash &&
                    token.Audience == audience &&
                    token.ConsumedAtUtc == null);

            if (resetToken?.User == null)
            {
                return null;
            }

            if (resetToken.ExpiresAtUtc <= now || !resetToken.User.IsActive)
            {
                return null;
            }

            if (!string.Equals(resetToken.User.UserType, "Customer", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resetToken.User.Role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return resetToken;
        }

        private async Task<int?> ResolveOwnerUserIdForCustomerInquiryAsync(int customerId)
        {
            if (customerId <= 0)
            {
                return await ResolveSingleActiveOwnerUserIdAsync();
            }

            var mostRecentOrderOwnerUserId = await _context.Orders
                .AsNoTracking()
                .Where(order => order.CustomerID == customerId && order.OwnerUserID.HasValue)
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.OrderID)
                .Select(order => order.OwnerUserID)
                .FirstOrDefaultAsync();

            if (mostRecentOrderOwnerUserId.HasValue && mostRecentOrderOwnerUserId.Value > 0)
            {
                return mostRecentOrderOwnerUserId.Value;
            }

            return await ResolveSingleActiveOwnerUserIdAsync();
        }

        private async Task<int?> ResolveSingleActiveOwnerUserIdAsync()
        {
            var activeOwnerUserIds = await _context.Users
                .AsNoTracking()
                .Where(user =>
                    user.IsActive &&
                    user.UserType == "Internal" &&
                    user.Role == "Owner")
                .OrderBy(user => user.UserID)
                .Select(user => user.UserID)
                .Take(2)
                .ToListAsync();

            return activeOwnerUserIds.Count == 1
                ? activeOwnerUserIds[0]
                : null;
        }

        private static string GetStockStatus(int stockQuantity)
        {
            if (stockQuantity <= 0)
            {
                return "Out of Stock";
            }

            return stockQuantity < 5 ? "Low Stock" : "In Stock";
        }
    }

    public class GoogleTokenInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("aud")]
        public string Aud { get; set; } = string.Empty;
    }
}

