using kstech.Data;
using kstech.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BCrypt.Net;

namespace kstech.Services
{
    public class AuthService : IAuthService
    {
        private const string AdminScheme = "AdminScheme";
        private const string CustomerScheme = "CustomerScheme";
        private const string SuperAdminOwnerScopeSessionKey = "Auth.AdminScheme.SuperAdmin.OwnerScopeUserId";
        private const string SuperAdminOwnerScopeCookieKey = "KSTech.SuperAdmin.OwnerScopeUserId";
        private const int MaxFailedLoginAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<User?> AuthenticateAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var normalizedEmail = email.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            if (user == null || !user.IsActive)
            {
                return null;
            }

            var ownerUserId = ResolveOwnerUserId(user);

            var now = DateTime.UtcNow;
            var userStateChanged = false;

            if (user.LockoutEnd.HasValue)
            {
                if (user.LockoutEnd.Value > now)
                {
                    _context.SystemLogs.Add(new SystemLog
                    {
                        UserID = user.UserID,
                        OwnerUserID = ownerUserId,
                        Action = "Login blocked: account is locked.",
                        Timestamp = now
                    });
                    await _context.SaveChangesAsync();
                    return null;
                }

                user.LockoutEnd = null;
                user.FailedLoginAttempts = 0;
                userStateChanged = true;
            }

            if (VerifyPassword(password, user.PasswordHash))
            {
                if (user.FailedLoginAttempts > 0 || user.LastFailedLogin.HasValue || userStateChanged)
                {
                    user.FailedLoginAttempts = 0;
                    user.LastFailedLogin = null;
                    user.LockoutEnd = null;
                    userStateChanged = true;
                }

                if (userStateChanged)
                {
                    await _context.SaveChangesAsync();
                }

                return user;
            }

            user.LastFailedLogin = now;
            user.FailedLoginAttempts += 1;

            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockoutEnd = now.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;

                _context.SystemLogs.Add(new SystemLog
                {
                    UserID = user.UserID,
                    OwnerUserID = ownerUserId,
                    Action = $"Account locked after {MaxFailedLoginAttempts} failed logins.",
                    Timestamp = now
                });
            }
            else
            {
                _context.SystemLogs.Add(new SystemLog
                {
                    UserID = user.UserID,
                    OwnerUserID = ownerUserId,
                    Action = $"Failed login attempt ({user.FailedLoginAttempts}/{MaxFailedLoginAttempts}).",
                    Timestamp = now
                });
            }

            await _context.SaveChangesAsync();
            return null;
        }

        public async Task<User> RegisterAsync(
            string email,
            string password,
            string fullName,
            string role,
            string userType,
            int? ownerUserId = null)
        {
            var normalizedEmail = email.Trim();
            if (await _context.Users.AnyAsync(u => u.Email == normalizedEmail))
            {
                throw new Exception("Email already exists.");
            }

            var user = new User
            {
                Email = normalizedEmail,
                PasswordHash = HashPassword(password),
                FullName = fullName,
                Role = role,
                UserType = userType,
                OwnerUserID = ownerUserId,
                DateCreated = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(userType, "Internal", StringComparison.OrdinalIgnoreCase) &&
                !user.OwnerUserID.HasValue)
            {
                user.OwnerUserID = user.UserID;
                await _context.SaveChangesAsync();
            }

            return user;
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool VerifyPassword(string password, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }

        public async Task SignInAsync(User user, bool isPersistent, string scheme)
        {
            var resolvedOwnerUserId = ResolveOwnerUserId(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("UserType", user.UserType),
                new Claim("FullName", user.FullName ?? string.Empty)
            };

            if (resolvedOwnerUserId.HasValue)
            {
                claims.Add(new Claim("OwnerUserID", resolvedOwnerUserId.Value.ToString()));
            }

            var claimsIdentity = new ClaimsIdentity(
                claims, scheme);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                AllowRefresh = true
            };

            if (isPersistent)
            {
                authProperties.ExpiresUtc = scheme switch
                {
                    AdminScheme => DateTimeOffset.UtcNow.AddHours(8),
                    CustomerScheme => DateTimeOffset.UtcNow.AddDays(30),
                    _ => DateTimeOffset.UtcNow.AddHours(8)
                };
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                await httpContext.SignInAsync(
                    scheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                StoreSignedInUserInSession(httpContext.Session, user, scheme, resolvedOwnerUserId);
                if (string.Equals(scheme, AdminScheme, StringComparison.Ordinal))
                {
                    httpContext.Response.Cookies.Delete(SuperAdminOwnerScopeCookieKey);
                }

                try
                {
                    _context.SystemLogs.Add(new SystemLog
                    {
                        UserID = user.UserID,
                        OwnerUserID = resolvedOwnerUserId,
                        Action = "Successful login.",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Authentication should not fail if audit logging fails.
                    _logger.LogWarning(
                        ex,
                        "Sign-in audit logging failed for user {UserId}.",
                        user.UserID);
                }
            }
        }

        public async Task SignOutAsync(string scheme)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                int? userId = null;
                var authResult = await httpContext.AuthenticateAsync(scheme);
                if (authResult.Succeeded)
                {
                    var userClaim = authResult.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(userClaim, out var parsedUserId))
                    {
                        userId = parsedUserId;
                    }
                }

                await httpContext.SignOutAsync(scheme);
                ClearSignedInUserFromSession(httpContext.Session, scheme);
                if (string.Equals(scheme, AdminScheme, StringComparison.Ordinal))
                {
                    httpContext.Response.Cookies.Delete(SuperAdminOwnerScopeCookieKey);
                }

                if (userId.HasValue)
                {
                    var userScope = await _context.Users
                        .Where(user => user.UserID == userId.Value)
                        .Select(user => new { user.OwnerUserID, user.Role })
                        .FirstOrDefaultAsync();

                    // Skip writing a logout audit row for stale auth identities where
                    // the referenced user no longer exists, to avoid FK violations.
                    if (userScope != null)
                    {
                        int? ownerUserId =
                            string.Equals(userScope.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : (userScope.OwnerUserID ?? userId.Value);

                        try
                        {
                            _context.SystemLogs.Add(new SystemLog
                            {
                                UserID = userId.Value,
                                OwnerUserID = ownerUserId,
                                Action = "User logged out.",
                                Timestamp = DateTime.UtcNow
                            });
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            // Logout should proceed even when audit log persistence fails.
                            _logger.LogWarning(
                                ex,
                                "Sign-out audit logging failed for user {UserId}.",
                                userId.Value);
                        }
                    }
                }
            }
        }

        private static void StoreSignedInUserInSession(ISession session, User user, string scheme, int? ownerUserId)
        {
            session.SetString(SessionKey(scheme, "UserId"), user.UserID.ToString());
            session.SetString(SessionKey(scheme, "FullName"), user.FullName ?? string.Empty);
            session.SetString(SessionKey(scheme, "Email"), user.Email ?? string.Empty);
            session.SetString(SessionKey(scheme, "Role"), user.Role ?? string.Empty);
            session.SetString(SessionKey(scheme, "UserType"), user.UserType ?? string.Empty);
            session.SetString(SessionKey(scheme, "OwnerUserID"), ownerUserId?.ToString() ?? string.Empty);

            // SuperAdmin should always start from global scope after sign in.
            if (string.Equals(scheme, AdminScheme, StringComparison.Ordinal))
            {
                session.Remove(SuperAdminOwnerScopeSessionKey);
            }
        }

        private static void ClearSignedInUserFromSession(ISession session, string scheme)
        {
            session.Remove(SessionKey(scheme, "UserId"));
            session.Remove(SessionKey(scheme, "FullName"));
            session.Remove(SessionKey(scheme, "Email"));
            session.Remove(SessionKey(scheme, "Role"));
            session.Remove(SessionKey(scheme, "UserType"));
            session.Remove(SessionKey(scheme, "OwnerUserID"));

            if (string.Equals(scheme, AdminScheme, StringComparison.Ordinal))
            {
                session.Remove(SuperAdminOwnerScopeSessionKey);
            }
        }

        private static string SessionKey(string scheme, string field)
        {
            return $"Auth.{scheme}.{field}";
        }

        private static int? ResolveOwnerUserId(User user)
        {
            if (string.Equals(user.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (user.OwnerUserID.HasValue)
            {
                return user.OwnerUserID.Value;
            }

            if (string.Equals(user.Role, "Owner", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(user.UserType, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                return user.UserID;
            }

            return null;
        }
    }
}
