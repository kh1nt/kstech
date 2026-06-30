using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using kstech.Data;

namespace kstech.Services
{
    public interface ITenantContext
    {
        int? CurrentUserId { get; }
        int? OwnerUserId { get; }
        bool HasOwnerScope { get; }
        bool CanEditOwnerWorkspace { get; }
        bool IsSuperAdmin { get; }
        bool IsAuthenticated { get; }
        bool SetSuperAdminOwnerScope(int ownerUserId, bool allowEdits);
        void ClearSuperAdminOwnerScope();
    }

    public class TenantContext : ITenantContext
    {
        private const string SuperAdminOwnerScopeSessionKey = "Auth.AdminScheme.SuperAdmin.OwnerScopeUserId";
        private const string SuperAdminOwnerScopeCookieKey = "KSTech.SuperAdmin.OwnerScopeUserId";
        private readonly IHttpContextAccessor _httpContextAccessor;
        private bool _canEditOwnerWorkspaceResolved;
        private bool _canEditOwnerWorkspace;

        public TenantContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int? CurrentUserId
        {
            get
            {
                var principal = _httpContextAccessor.HttpContext?.User;
                if (principal == null)
                {
                    return null;
                }

                var claimValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(claimValue, out var userId) ? userId : null;
            }
        }

        public int? OwnerUserId
        {
            get
            {
                if (IsSuperAdmin)
                {
                    return ResolveSuperAdminOwnerScopeUserId();
                }

                var principal = _httpContextAccessor.HttpContext?.User;
                if (principal == null)
                {
                    return null;
                }

                var role = principal.FindFirst(ClaimTypes.Role)?.Value;
                if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var claimValue = principal.FindFirst("OwnerUserID")?.Value;
                if (int.TryParse(claimValue, out var ownerUserId))
                {
                    return ownerUserId;
                }

                return CurrentUserId;
            }
        }

        public bool HasOwnerScope => OwnerUserId.HasValue;

        public bool CanEditOwnerWorkspace
        {
            get
            {
                if (_canEditOwnerWorkspaceResolved)
                {
                    return _canEditOwnerWorkspace;
                }

                if (!IsAuthenticated)
                {
                    _canEditOwnerWorkspace = false;
                    _canEditOwnerWorkspaceResolved = true;
                    return _canEditOwnerWorkspace;
                }

                if (!IsSuperAdmin)
                {
                    _canEditOwnerWorkspace = true;
                    _canEditOwnerWorkspaceResolved = true;
                    return _canEditOwnerWorkspace;
                }

                if (!OwnerUserId.HasValue)
                {
                    _canEditOwnerWorkspace = false;
                    _canEditOwnerWorkspaceResolved = true;
                    return _canEditOwnerWorkspace;
                }

                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null)
                {
                    _canEditOwnerWorkspace = false;
                }
                else
                {
                    var sessionValue = httpContext.Session.GetString("Auth.AdminScheme.SuperAdmin.OwnerScopeAllowEdits");
                    if (bool.TryParse(sessionValue, out var allowEdits))
                    {
                        _canEditOwnerWorkspace = allowEdits;
                    }
                    else if (httpContext.Request.Cookies.TryGetValue("KSTech.SuperAdmin.OwnerScopeAllowEdits", out var cookieValue) &&
                             bool.TryParse(cookieValue, out allowEdits))
                    {
                        httpContext.Session.SetString("Auth.AdminScheme.SuperAdmin.OwnerScopeAllowEdits", allowEdits.ToString());
                        _canEditOwnerWorkspace = allowEdits;
                    }
                    else
                    {
                        _canEditOwnerWorkspace = false;
                    }
                }

                _canEditOwnerWorkspaceResolved = true;
                return _canEditOwnerWorkspace;
            }
        }

        public bool IsSuperAdmin =>
            _httpContextAccessor.HttpContext?.User?.IsInRole("SuperAdmin") == true;

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public bool SetSuperAdminOwnerScope(int ownerUserId, bool allowEdits)
        {
            if (ownerUserId <= 0 || !IsSuperAdmin)
            {
                return false;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return false;
            }

            var ownerUserIdText = ownerUserId.ToString();
            httpContext.Session.SetString(SuperAdminOwnerScopeSessionKey, ownerUserIdText);
            httpContext.Session.SetString("Auth.AdminScheme.SuperAdmin.OwnerScopeAllowEdits", allowEdits.ToString());

            httpContext.Response.Cookies.Append(
                SuperAdminOwnerScopeCookieKey,
                ownerUserIdText,
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromHours(8)
                });

            httpContext.Response.Cookies.Append(
                "KSTech.SuperAdmin.OwnerScopeAllowEdits",
                allowEdits.ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromHours(8)
                });

            ResetEditPermissionCache();
            return true;
        }

        public void ClearSuperAdminOwnerScope()
        {
            if (!IsSuperAdmin)
            {
                return;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return;
            }

            httpContext.Session.Remove(SuperAdminOwnerScopeSessionKey);
            httpContext.Session.Remove("Auth.AdminScheme.SuperAdmin.OwnerScopeAllowEdits");
            httpContext.Response.Cookies.Delete(SuperAdminOwnerScopeCookieKey);
            httpContext.Response.Cookies.Delete("KSTech.SuperAdmin.OwnerScopeAllowEdits");
            ResetEditPermissionCache();
        }

        private int? ResolveSuperAdminOwnerScopeUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            var scopeValue = httpContext.Session.GetString(SuperAdminOwnerScopeSessionKey);
            if (int.TryParse(scopeValue, out var ownerUserId) && ownerUserId > 0)
            {
                return ownerUserId;
            }

            if (httpContext.Request.Cookies.TryGetValue(SuperAdminOwnerScopeCookieKey, out var scopeCookieValue) &&
                int.TryParse(scopeCookieValue, out ownerUserId) &&
                ownerUserId > 0)
            {
                httpContext.Session.SetString(SuperAdminOwnerScopeSessionKey, ownerUserId.ToString());
                return ownerUserId;
            }

            return null;
        }

        private void ResetEditPermissionCache()
        {
            _canEditOwnerWorkspaceResolved = false;
            _canEditOwnerWorkspace = false;
        }
    }
}
