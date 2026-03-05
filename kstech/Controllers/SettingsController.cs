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
                user.AllowSuperAdminWorkspaceEdits = profileModel.AllowSuperAdminWorkspaceEdits;
            }

            if (canConfigureSuperAdminWorkspaceAccess && emailChanged)
            {
                user.IsEmailVerified = false;
                user.EmailVerificationToken = Guid.NewGuid().ToString("N");
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

            if (canConfigureSuperAdminWorkspaceAccess && emailChanged)
            {
                var token = user.EmailVerificationToken;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await QueueOwnerVerificationEmailAsync(user, token);
                }

                await _authService.SignOutAsync(AdminScheme);
                TempData["SuccessMessage"] = "Profile updated. Verify your new email address before signing in again.";
                return RedirectToAction("Login", "Account");
            }

            HttpContext.Session.SetString("Auth.AdminScheme.FullName", user.FullName ?? string.Empty);
            HttpContext.Session.SetString("Auth.AdminScheme.Email", user.Email ?? string.Empty);

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index), new { section = "profile" });
        }

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
                Security = new ChangePasswordViewModel()
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
