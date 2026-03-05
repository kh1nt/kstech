using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using kstech.Models.Entities;
using kstech.Models.ViewModels;
using kstech.Utilities;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin,Owner")]
    public class ActivityLogsController : Controller
    {
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly kstech.Services.ITenantContext _tenantContext;
        private static readonly int[] AllowedPageSizes = { 10, 20, 50 };

        public ActivityLogsController(
            kstech.Data.ApplicationDbContext context,
            kstech.Services.ITenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        // Action of Index
        [HttpGet]
        public IActionResult Index(
            string? search = null,
            string? role = null,
            int days = 7,
            int page = 1,
            int pageSize = 10)
        {
            var normalizedSearch = (search ?? string.Empty).Trim();
            var normalizedDays = NormalizeDays(days);
            var normalizedPageSize = NormalizePageSize(pageSize);

            var baseQuery = BuildFilteredQuery(normalizedSearch, normalizedDays);
            var roleOptions = BuildRoleOptions(baseQuery);
            var normalizedRole = NormalizeRole(role, roleOptions);
            var matchingQuery = ApplyRoleFilter(baseQuery, normalizedRole);

            var totalMatched = matchingQuery.Count();
            var totalPages = totalMatched <= 0
                ? 1
                : (int)Math.Ceiling(totalMatched / (double)normalizedPageSize);
            var normalizedPage = NormalizePage(page, totalPages);

            var displayedLogs = matchingQuery
                .OrderByDescending(log => log.Timestamp)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Include(log => log.User)
                .ToList()
                .Select(MapToActivityLogItem)
                .ToList();

            var groupedRoleCounts = matchingQuery
                .GroupBy(log => log.User != null && !string.IsNullOrWhiteSpace(log.User.Role) ? log.User.Role : "System")
                .Select(group => new { Role = group.Key, Count = group.Count() })
                .ToDictionary(group => group.Role, group => group.Count, StringComparer.OrdinalIgnoreCase);

            var viewModel = new ActivityLogIndexViewModel
            {
                Search = normalizedSearch,
                Role = normalizedRole,
                Days = normalizedDays,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                TotalMatched = totalMatched,
                OwnerEvents = CountByRole(groupedRoleCounts, "Owner"),
                SuperAdminEvents = CountByRole(groupedRoleCounts, "SuperAdmin"),
                InventoryManagerEvents = CountByRole(groupedRoleCounts, "Inventory Manager"),
                SalesStaffEvents = CountByRole(groupedRoleCounts, "Sales Staff"),
                Roles = roleOptions,
                Logs = displayedLogs
            };

            return View(viewModel);
        }

        // Action of ExportCsv
        [HttpGet]
        public IActionResult ExportCsv(string? search = null, string? role = null, int days = 7)
        {
            var normalizedSearch = (search ?? string.Empty).Trim();
            var normalizedDays = NormalizeDays(days);
            var baseQuery = BuildFilteredQuery(normalizedSearch, normalizedDays);
            var roleOptions = BuildRoleOptions(baseQuery);
            var normalizedRole = NormalizeRole(role, roleOptions);
            var logs = ApplyRoleFilter(baseQuery, normalizedRole)
                .OrderByDescending(log => log.Timestamp)
                .Include(log => log.User)
                .ToList()
                .Select(MapToActivityLogItem)
                .ToList();

            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,RelativeTime,Role,User,Action");

            foreach (var log in logs)
            {
                csv.AppendLine(string.Join(",",
                    EscapeForCsv(BusinessTime.ConvertUtcToBusinessTime(log.Timestamp).ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeForCsv(log.RelativeTime),
                    EscapeForCsv(log.UserRole),
                    EscapeForCsv(log.UserDisplay),
                    EscapeForCsv(log.Action)));
            }

            var fileName = $"activity-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }

        private IQueryable<SystemLog> BuildFilteredQuery(string search, int days)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var query = _context.SystemLogs
                .AsNoTracking()
                .Where(log => log.Timestamp >= cutoff)
                .Where(log => !applyOwnerFilter || log.OwnerUserID == ownerUserId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(log =>
                    log.Action.Contains(search) ||
                    (log.User != null &&
                        (log.User.FullName.Contains(search) ||
                         log.User.Role.Contains(search))));
            }

            return query;
        }

        private static IQueryable<SystemLog> ApplyRoleFilter(IQueryable<SystemLog> query, string role)
        {
            if (string.Equals(role, "All", StringComparison.OrdinalIgnoreCase))
            {
                return query;
            }

            if (string.Equals(role, "System", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(log => log.User == null || string.IsNullOrWhiteSpace(log.User.Role));
            }

            return query.Where(log => log.User != null && log.User.Role == role);
        }

        private static List<string> BuildRoleOptions(IQueryable<SystemLog> query)
        {
            var roles = query
                .Select(log => log.User != null ? log.User.Role : null)
                .ToList()
                .Select(role => string.IsNullOrWhiteSpace(role) ? "System" : role.Trim())
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!roles.Any(role => string.Equals(role, "System", StringComparison.OrdinalIgnoreCase)))
            {
                roles.Add("System");
            }

            roles = roles
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();

            roles.Insert(0, "All");
            return roles;
        }

        private static int CountByRole(IReadOnlyDictionary<string, int> groupedRoleCounts, string role)
        {
            return groupedRoleCounts.TryGetValue(role, out var count) ? count : 0;
        }

        private static ActivityLogItemViewModel MapToActivityLogItem(SystemLog log)
        {
            return new ActivityLogItemViewModel
            {
                LogId = log.LogID,
                Timestamp = log.Timestamp,
                RelativeTime = FormatRelativeTime(log.Timestamp),
                UserRole = string.IsNullOrWhiteSpace(log.User?.Role) ? "System" : log.User.Role,
                UserDisplay = string.IsNullOrWhiteSpace(log.User?.FullName) ? "System" : log.User.FullName,
                Action = log.Action ?? string.Empty
            };
        }

        private static int NormalizeDays(int days)
        {
            if (days <= 0)
            {
                return 7;
            }

            return days switch
            {
                1 => 1,
                7 => 7,
                30 => 30,
                90 => 90,
                _ => 7
            };
        }

        private static int NormalizePageSize(int pageSize)
        {
            return AllowedPageSizes.Contains(pageSize) ? pageSize : 10;
        }

        private static int NormalizePage(int page, int totalPages)
        {
            if (page <= 0)
            {
                return 1;
            }

            if (page > totalPages)
            {
                return totalPages;
            }

            return page;
        }

        private static string NormalizeRole(string? role, List<string> roleOptions)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "All";
            }

            var normalized = role.Trim();

            return roleOptions.Any(option => string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase))
                ? roleOptions.First(option => string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase))
                : "All";
        }

        private static string FormatRelativeTime(DateTime timestamp)
        {
            var span = DateTime.UtcNow - timestamp;

            if (span.TotalMinutes < 1)
            {
                return "just now";
            }

            if (span.TotalHours < 1)
            {
                return $"{(int)span.TotalMinutes}m ago";
            }

            if (span.TotalDays < 1)
            {
                return $"{(int)span.TotalHours}h ago";
            }

            if (span.TotalDays < 30)
            {
                return $"{(int)span.TotalDays}d ago";
            }

            return BusinessTime.ConvertUtcToBusinessTime(timestamp).ToString("MMM dd, yyyy");
        }

        private static string EscapeForCsv(string value)
        {
            var escaped = (value ?? string.Empty).Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }
    }
}

