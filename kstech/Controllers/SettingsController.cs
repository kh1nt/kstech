using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using kstech.Models.ViewModels;
using kstech.Models.Entities;
using kstech.Services;
using System.Security.Claims;
using System.Net;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme")]
    public class SettingsController : Controller
    {
        private const string AdminScheme = "AdminScheme";
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly IAuthService _authService;
        private readonly IEmailOutboxService _emailOutboxService;

        public SettingsController(
            kstech.Data.ApplicationDbContext context,
            IAuthService authService,
            IEmailOutboxService emailOutboxService)
        {
            _context = context;
            _authService = authService;
            _emailOutboxService = emailOutboxService;
        }

        // Action of Index
        public async Task<IActionResult> Index(string section = "profile")
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var model = await BuildSettingsPageViewModelAsync(userId.Value);
            if (model == null)
            {
                return RedirectToAction("Login", "Account");
            }

            model.ActiveSection = string.Equals(section, "security", StringComparison.OrdinalIgnoreCase)
                ? "security"
                : "profile";

            return View(model);
        }

        // Action of UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Profile")] UserProfileViewModel profileModel)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Profile = profileModel;
                invalidModel.ActiveSection = "profile";
                return View("Index", invalidModel);
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            var normalizedEmail = profileModel.Email.Trim();
            var emailAlreadyInUse = await _context.Users
                .AnyAsync(existingUser => existingUser.UserID != user.UserID && existingUser.Email == normalizedEmail);

            if (emailAlreadyInUse)
            {
                ModelState.AddModelError("Profile.Email", "This email is already in use.");
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Profile = profileModel;
                invalidModel.ActiveSection = "profile";
                return View("Index", invalidModel);
            }

            var emailChanged = !string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase);
            user.Email = normalizedEmail;
            user.FullName = profileModel.FullName.Trim();
            var normalizedContactNumber = profileModel.ContactNumber.Trim();
            var canConfigureSuperAdminWorkspaceAccess =
                string.Equals(user.Role, "Owner", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase);

            if (canConfigureSuperAdminWorkspaceAccess)
            {
                var oldAllowEdits = user.AllowSuperAdminWorkspaceEdits;
                user.AllowSuperAdminWorkspaceEdits = profileModel.AllowSuperAdminWorkspaceEdits;
                if (oldAllowEdits != profileModel.AllowSuperAdminWorkspaceEdits)
                {
                    var actorUserId = GetCurrentUserId() ?? user.UserID;
                    _context.SystemLogs.Add(new kstech.Models.Entities.SystemLog
                    {
                        UserID = actorUserId,
                        OwnerUserID = user.UserID,
                        Action = $"AllowSuperAdminWorkspaceEdits changed from {oldAllowEdits} to {profileModel.AllowSuperAdminWorkspaceEdits} for user {user.Email}.",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            if (canConfigureSuperAdminWorkspaceAccess && emailChanged)
            {
                var rawToken = kstech.Services.PasswordResetTokenHelper.GenerateRawToken(16);
                user.IsEmailVerified = false;
                // Store a SHA-256 hash; the raw token is passed to the email link
                user.EmailVerificationToken = kstech.Services.PasswordResetTokenHelper.HashOtp(rawToken);

                await _context.SaveChangesAsync();

                await QueueOwnerVerificationEmailAsync(user, rawToken);
                await _authService.SignOutAsync(AdminScheme);
                TempData["SuccessMessage"] = "Profile updated. Verify your new email address before signing in again.";
                return RedirectToAction("Login", "Account");
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.UserID == user.UserID);
            if (employee != null)
            {
                employee.FullName = user.FullName;
                employee.ContactNumber = normalizedContactNumber;
            }

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.UserID);
            if (customer != null)
            {
                customer.Email = user.Email;
                customer.FullName = user.FullName;
                customer.Phone = normalizedContactNumber;
            }

            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("Auth.AdminScheme.FullName", user.FullName ?? string.Empty);
            HttpContext.Session.SetString("Auth.AdminScheme.Email", user.Email ?? string.Empty);

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index), new { section = "profile" });
        }

        /* Duplicated method
        // Action of ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "Security")] ChangePasswordViewModel securityModel)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!_authService.VerifyPassword(securityModel.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError("Security.CurrentPassword", "Current password is incorrect.");
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }

            if (_authService.VerifyPassword(securityModel.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError("Security.NewPassword", "New password must be different from current password.");
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }


            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index), new { section = "profile" });
        }*/

        // Action of ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "Security")] ChangePasswordViewModel securityModel)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            if (!_authService.VerifyPassword(securityModel.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError("Security.CurrentPassword", "Current password is incorrect.");
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }

            if (_authService.VerifyPassword(securityModel.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError("Security.NewPassword", "New password must be different from current password.");
                var invalidModel = await BuildSettingsPageViewModelAsync(userId.Value);
                if (invalidModel == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                invalidModel.Security = securityModel;
                invalidModel.ActiveSection = "security";
                return View("Index", invalidModel);
            }

            user.PasswordHash = _authService.HashPassword(securityModel.NewPassword);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index), new { section = "security" });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> EnableMfa(string secret, string code)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            secret = (secret ?? string.Empty).Trim();
            code = (code ?? string.Empty).Trim();

            if (kstech.Utilities.TotpHelper.VerifyCode(secret, code))
            {
                user.TwoFactorEnabled = true;
                user.TwoFactorSecret = secret;

                var rawBackupCodes = new List<string>();
                var hashedBackupCodes = new List<string>();
                for (int i = 0; i < 10; i++)
                {
                    var rawCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
                    rawBackupCodes.Add(rawCode);
                    hashedBackupCodes.Add(_authService.HashPassword(rawCode));
                }
                user.TwoFactorBackupCodes = string.Join(";", hashedBackupCodes);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Multi-Factor Authentication enabled successfully. Please copy and save your backup codes below!";
                TempData["BackupCodes"] = rawBackupCodes;
            }
            else
            {
                TempData["ErrorMessage"] = "Invalid verification code. Please try again.";
            }

            return RedirectToAction(nameof(Index), new { section = "security" });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DisableMfa(string password)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            password = password ?? string.Empty;
            if (_authService.VerifyPassword(password, user.PasswordHash))
            {
                user.TwoFactorEnabled = false;
                user.TwoFactorSecret = null;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Multi-Factor Authentication disabled successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Incorrect password. MFA could not be disabled.";
            }

            return RedirectToAction(nameof(Index), new { section = "security" });
        }

        private int? GetCurrentUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
            {
                return null;
            }

            return userId;
        }

        private async Task<SettingsPageViewModel?> BuildSettingsPageViewModelAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return null;
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.UserID == user.UserID);
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.UserID);
            var contactNumber = employee?.ContactNumber;
            if (string.IsNullOrWhiteSpace(contactNumber))
            {
                contactNumber = customer?.Phone;
            }

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

            return new SettingsPageViewModel
            {
                Profile = new UserProfileViewModel
                {
                    UserId = user.UserID,
                    Email = user.Email,
                    FullName = user.FullName,
                    ContactNumber = contactNumber ?? string.Empty,
                    Role = user.Role,
                    AllowSuperAdminWorkspaceEdits = user.AllowSuperAdminWorkspaceEdits,
                    CanConfigureSuperAdminWorkspaceAccess =
                        string.Equals(user.Role, "Owner", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase)
                },
                Security = new ChangePasswordViewModel(),
                TwoFactorEnabled = user.TwoFactorEnabled,
                TwoFactorSecret = secret,
                TwoFactorQrUrl = qrUrl
            };
        }

        private async Task QueueOwnerVerificationEmailAsync(User user, string token)
        {
            if (string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var verificationLink = Url.Action("VerifyEmail", "Account", new { token }, Request.Scheme);
            if (string.IsNullOrWhiteSpace(verificationLink))
            {
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
    }
}
