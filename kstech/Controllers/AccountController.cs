using kstech.Models;
using kstech.Models.Entities;
using kstech.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

namespace kstech.Controllers
{
    public class AccountController : Controller
    {
        private const string AdminScheme = "AdminScheme";
        private const string ExternalScheme = "ExternalScheme";
        private const string AdminPasswordResetAudience = "Admin";
        private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);

        private readonly IAuthService _authService;
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly IEmailOutboxService _emailOutboxService;
        private readonly IConfiguration _configuration;

        public AccountController(
            IAuthService authService,
            kstech.Data.ApplicationDbContext context,
            ILogger<AccountController> logger,
            IEmailOutboxService emailOutboxService,
            IConfiguration configuration)
        {
            _authService = authService;
            _context = context;
            _logger = logger;
            _emailOutboxService = emailOutboxService;
            _configuration = configuration;
        }

        // Action of Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            HttpContext.Session.Remove("Auth.AdminScheme.SuperAdmin.OwnerScopeUserId");
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

            // Enforce Internal-only login on the admin side
            if (user.UserType != "Internal")
            {
                ViewBag.Error = "This login is for staff only. Customers please use the store login.";
                return View();
            }

            if (RequiresOwnerEmailVerification(user) && !user.IsEmailVerified)
            {
                await QueueOwnerVerificationEmailAsync(user);
                ViewBag.Error = "Please verify your owner email address before signing in. A verification link was sent to your inbox.";
                return View();
            }

            var shouldUsePersistentSession = rememberMe || ShouldPersistStaffSessionByDefault(user);

            if (user.TwoFactorEnabled)
            {
                HttpContext.Session.SetString("MfaTemp.UserId", user.UserID.ToString());
                HttpContext.Session.SetString("MfaTemp.RememberMe", shouldUsePersistentSession.ToString());
                HttpContext.Session.SetString("MfaTemp.ReturnUrl", returnUrl ?? "");
                return RedirectToAction(nameof(VerifyMfa));
            }

            await _authService.SignInAsync(user, isPersistent: shouldUsePersistentSession, scheme: AdminScheme);
            _logger.LogInformation($"User {email} logged in successfully.");

            return RedirectAfterAdminLogin(user, returnUrl);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyMfa()
        {
            var userIdStr = HttpContext.Session.GetString("MfaTemp.UserId");
            if (string.IsNullOrWhiteSpace(userIdStr))
            {
                return RedirectToAction(nameof(Login));
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> VerifyMfa(string code)
        {
            var userIdStr = HttpContext.Session.GetString("MfaTemp.UserId");
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

            var rememberMeStr = HttpContext.Session.GetString("MfaTemp.RememberMe");
            bool.TryParse(rememberMeStr, out var rememberMe);
            var returnUrl = HttpContext.Session.GetString("MfaTemp.ReturnUrl");

            HttpContext.Session.Remove("MfaTemp.UserId");
            HttpContext.Session.Remove("MfaTemp.RememberMe");
            HttpContext.Session.Remove("MfaTemp.ReturnUrl");

            await _authService.SignInAsync(user, isPersistent: rememberMe, scheme: AdminScheme);
            _logger.LogInformation("User {Email} signed in via admin MFA.", user.Email);

            return RedirectAfterAdminLogin(user, returnUrl);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult VerifyCode(string email)
        {
            ViewData["Email"] = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
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
            user.EmailVerificationToken = null;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Email verified successfully! You can now log in.";
            return RedirectToAction(nameof(Login));
        }

        // Action of GoogleLogin
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin(string? returnUrl = null)
        {
            if (!IsGoogleLoginConfigured())
            {
                TempData["ErrorMessage"] = "Google login is not configured yet. Add Authentication:Google:ClientId and ClientSecret.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            await HttpContext.SignOutAsync(ExternalScheme);

            var redirectUrl = Url.Action(nameof(GoogleLoginCallback), "Account", new { returnUrl });
            if (string.IsNullOrWhiteSpace(redirectUrl))
            {
                TempData["ErrorMessage"] = "Unable to start Google login.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var authProperties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            return Challenge(authProperties, GoogleDefaults.AuthenticationScheme);
        }

        // Action of GoogleLoginCallback
        [HttpGet]
        [AllowAnonymous]
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

            var email =
                externalAuth.Principal.FindFirst(ClaimTypes.Email)?.Value ??
                externalAuth.Principal.FindFirst("email")?.Value;

            await HttpContext.SignOutAsync(ExternalScheme);

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Google account did not provide an email address.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var normalizedEmail = email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.IsActive && u.Email == normalizedEmail);
            if (user == null)
            {
                TempData["ErrorMessage"] = "No staff account is linked to that Google email. Use your password login or ask an admin to create the account first.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (!string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "This login is for staff only. Customers please use the store login.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (RequiresOwnerEmailVerification(user) && !user.IsEmailVerified)
            {
                await QueueOwnerVerificationEmailAsync(user);
                TempData["ErrorMessage"] = "Please verify your owner email address before signing in. A verification link was sent to your inbox.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "Your account is temporarily locked. Please try again later or use password reset.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            await _authService.SignInAsync(user, isPersistent: true, scheme: AdminScheme);
            _logger.LogInformation("User {Email} logged in successfully via Google.", normalizedEmail);

            return RedirectAfterAdminLogin(user, returnUrl);
        }

        // Action of Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _authService.SignOutAsync(AdminScheme);
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Login", "Account");
        }

        // Action of AccessDenied
        [HttpGet]
        public IActionResult AccessDenied(string? returnUrl = null)
        {
            TempData["ErrorMessage"] = "You do not have permission to access that page.";

            if (User.IsInRole("SuperAdmin"))
            {
                return RedirectToAction("Index", "Owner");
            }

            if (User.IsInRole("Inventory Manager"))
            {
                return RedirectToAction("Index", "Inventory");
            }

            if (User.IsInRole("Sales Staff"))
            {
                return RedirectToAction("Index", "CRM");
            }

            if (User.IsInRole("Owner"))
            {
                return RedirectToAction("Index", "Home");
            }

            return RedirectToAction(nameof(Login));
        }

        // Action of ForgotPassword
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // Action of ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
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
                await QueueInternalPasswordResetEmailIfUserExistsAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process forgot-password request for internal email {Email}", email);
            }

            TempData["SuccessMessage"] = "If an account exists for that email, a password reset link has been sent.";
            return RedirectToAction(nameof(Login));
        }

        // Action of ResetPassword
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(string token)
        {
            var resetToken = await GetValidPasswordResetTokenAsync(token, AdminPasswordResetAudience);
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
        [AllowAnonymous]
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

            var resetToken = await GetValidPasswordResetTokenAsync(token, AdminPasswordResetAudience);
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
                    item.Audience == AdminPasswordResetAudience &&
                    item.ConsumedAtUtc == null)
                .ToListAsync();

            foreach (var item in activeResetTokens)
            {
                item.ConsumedAtUtc = now;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your password has been reset successfully. You can now log in.";
            return RedirectToAction(nameof(Login));
        }

        // Action of Register
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            token = (token ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid verification link.";
                return RedirectToAction(nameof(Login));
            }

            var tokenHash = kstech.Services.PasswordResetTokenHelper.HashOtp(token);
            var user = await _context.Users.FirstOrDefaultAsync(existing =>
                existing.IsActive &&
                existing.EmailVerificationToken == tokenHash);

            if (user == null || !RequiresOwnerEmailVerification(user))
            {
                TempData["ErrorMessage"] = "Verification link has expired or is invalid.";
                return RedirectToAction(nameof(Login));
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "Your owner email is already verified.";
                return RedirectToAction(nameof(Login));
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Owner email verified successfully. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        // Action of Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AuthStrict")]
        public async Task<IActionResult> Register(string fullName, string email, string password, string confirmPassword)
        {
            fullName = (fullName ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            password = password ?? string.Empty;
            confirmPassword = confirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(confirmPassword))
            {
                ViewBag.Error = "All fields are required.";
                return View();
            }

            if (!PasswordPolicy.TryValidate(password, out var passwordPolicyError))
            {
                ViewBag.Error = passwordPolicyError;
                return View();
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            try
            {
                // Account registration is for internal users only.
                var user = await _authService.RegisterAsync(
                    email,
                    password,
                    fullName,
                    role: "Owner",
                    userType: "Internal");

                var ownerUserId = user.OwnerUserID ?? user.UserID;
                if (!await _context.Employees.AnyAsync(employee => employee.UserID == user.UserID))
                {
                    _context.Employees.Add(new Employee
                    {
                        UserID = user.UserID,
                        OwnerUserID = ownerUserId,
                        FullName = user.FullName,
                        Position = "Owner",
                        ContactNumber = string.Empty,
                        HireDate = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();
                }

                await QueueOwnerVerificationEmailAsync(user, forceNewToken: true);
                _logger.LogInformation("Internal owner account registered and verification queued: {Email}", email);

                TempData["SuccessMessage"] = "Account created successfully. Verify your email first before signing in.";
                return RedirectToAction(nameof(Login));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register internal account for {Email}", email);
                ViewBag.Error = ex.Message;
                return View();
            }
        }

        private async Task QueueInternalPasswordResetEmailIfUserExistsAsync(string email)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(existing =>
                    existing.IsActive &&
                    existing.UserType == "Internal" &&
                    existing.Email == email);

            if (user == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var activeTokens = await _context.PasswordResetTokens
                .Where(token =>
                    token.UserID == user.UserID &&
                    token.Audience == AdminPasswordResetAudience &&
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
                Audience = AdminPasswordResetAudience,
                TokenHash = PasswordResetTokenHelper.HashToken(rawToken),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(PasswordResetTokenLifetime)
            });

            await _context.SaveChangesAsync();

            var resetLink = Url.Action(nameof(ResetPassword), "Account", new { token = rawToken }, Request.Scheme);
            if (string.IsNullOrWhiteSpace(resetLink))
            {
                _logger.LogWarning("Failed to generate internal password reset link for user {UserId}", user.UserID);
                return;
            }

            var encodedLink = WebUtility.HtmlEncode(resetLink);
            var encodedEmail = WebUtility.HtmlEncode(user.Email);

            await _emailOutboxService.QueueEmailAsync(
                user.Email,
                "Reset your KSTech password",
                $"""
                <p>A password reset was requested for your KSTech account ({encodedEmail}).</p>
                <p><a href="{encodedLink}">Click here to reset your password</a>.</p>
                <p>This link expires in {(int)PasswordResetTokenLifetime.TotalMinutes} minutes. If you did not request this, you can ignore this email.</p>
                """,
                user.OwnerUserID ?? user.UserID);
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

            if (!string.Equals(resetToken.User.UserType, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return resetToken;
        }

        private IActionResult RedirectAfterAdminLogin(User user, string? returnUrl)
        {
            // SuperAdmin always starts in Owner monitoring (global mode).
            var role = user.Role?.Trim();
            if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Owner");
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Route staff to their default module when no returnUrl is provided.
            if (string.Equals(role, "Inventory Manager", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Inventory");
            }

            if (string.Equals(role, "Sales Staff", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "CRM");
            }

            if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Home");
            }

            return RedirectToAction("Index", "Settings");
        }

        private async Task QueueOwnerVerificationEmailAsync(User user, bool forceNewToken = false)
        {
            if (!RequiresOwnerEmailVerification(user) || string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            string rawToken;
            if (forceNewToken || string.IsNullOrWhiteSpace(user.EmailVerificationToken))
            {
                rawToken = Guid.NewGuid().ToString("N");
                user.IsEmailVerified = false;
                user.EmailVerificationToken = kstech.Services.PasswordResetTokenHelper.HashOtp(rawToken);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Token already set but un-consumed — generate fresh so we can re-send.
                rawToken = Guid.NewGuid().ToString("N");
                user.EmailVerificationToken = kstech.Services.PasswordResetTokenHelper.HashOtp(rawToken);
                await _context.SaveChangesAsync();
            }

            var token = rawToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var verificationLink = Url.Action(nameof(VerifyEmail), "Account", new { token }, Request.Scheme);
            if (string.IsNullOrWhiteSpace(verificationLink))
            {
                _logger.LogWarning("Failed to generate owner verification link for user {UserId}", user.UserID);
                return;
            }

            var encodedLink = WebUtility.HtmlEncode(verificationLink);
            var encodedName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(user.FullName) ? "Owner" : user.FullName);

            await _emailOutboxService.QueueEmailAsync(
                user.Email,
                "Verify your KSTech owner account",
                $"""
                <p>Hi {encodedName},</p>
                <p>Please <a href="{encodedLink}">click here to verify your owner account email</a>.</p>
                <p>You must verify your email before you can sign in to the admin workspace.</p>
                """,
                user.OwnerUserID ?? user.UserID);
        }

        private static bool RequiresOwnerEmailVerification(User user)
        {
            return string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(user.Role, "Owner", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldPersistStaffSessionByDefault(User user)
        {
            if (!string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(user.Role, "Inventory Manager", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(user.Role, "Sales Staff", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGoogleLoginConfigured()
        {
            var clientId = _configuration["Authentication:Google:ClientId"];
            var clientSecret = _configuration["Authentication:Google:ClientSecret"];
            return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
        }
    }
}
