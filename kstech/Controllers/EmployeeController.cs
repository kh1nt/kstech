using kstech.Models;
using kstech.Models.Entities;
using kstech.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kstech.Services;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin,Owner")]
    [RequireOwnerScopeForSuperAdmin]
    public class EmployeeController : Controller
    {
        private static readonly HashSet<string> AllowedEmployeeRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Inventory Manager",
            "Sales Staff"
        };

        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly IAuthService _authService;
        private readonly ITenantContext _tenantContext;

        public EmployeeController(
            kstech.Data.ApplicationDbContext context,
            IAuthService authService,
            ITenantContext tenantContext)
        {
            _context = context;
            _authService = authService;
            _tenantContext = tenantContext;
        }

        // Action of Index
        public async Task<IActionResult> Index(string status = "active")
        {
            var normalizedStatus = NormalizeStatusFilter(status);
            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;

            var employeesQuery = _context.Employees
                .Include(e => e.User)
                .Where(e => e.User != null)
                .Where(e => e.Position == "Inventory Manager" || e.Position == "Sales Staff")
                .Where(e => !applyOwnerFilter || e.OwnerUserID == ownerUserId);

            employeesQuery = normalizedStatus switch
            {
                "archived" => employeesQuery.Where(e => e.IsArchived),
                "inactive" => employeesQuery.Where(e => !e.IsArchived && e.User != null && !e.User.IsActive),
                _ => employeesQuery.Where(e => !e.IsArchived && e.User != null && e.User.IsActive)
            };

            var employees = await employeesQuery
                .Select(e => new EmployeeViewModel
                {
                    Id = e.EmpID,
                    Name = e.FullName,
                    Email = e.User != null ? e.User.Email : string.Empty,
                    Role = e.Position,
                    ImageUrl = null, // Future: Add image support
                    Status = e.IsArchived ? "Archived" : (e.User != null && e.User.IsActive ? "Active" : "Inactive"),
                    IsArchived = e.IsArchived,
                    ActiveStatusTime = e.IsArchived ? "Archived" : (e.User != null && e.User.IsActive ? "Active" : "Inactive"),
                    IsActiveNow = !e.IsArchived && e.User != null && e.User.IsActive,
                    Tags = GetTagsForRole(e.Position),
                    TagColorClass = GetTagColorForRole(e.Position)
                }).ToListAsync();

            // Fetch Real Role Counts (Active only)
            var inventoryCount = await _context.Employees
                .Include(e => e.User)
                .CountAsync(e =>
                    e.Position == "Inventory Manager" &&
                    !e.IsArchived &&
                    e.User != null &&
                    e.User.IsActive &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            var salesCount = await _context.Employees
                .Include(e => e.User)
                .CountAsync(e =>
                    e.Position == "Sales Staff" &&
                    !e.IsArchived &&
                    e.User != null &&
                    e.User.IsActive &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            // Fetch Initials for Roles
            var inventoryInitials = await _context.Employees
                .Include(e => e.User)
                .Where(e => e.Position == "Inventory Manager" && !e.IsArchived && e.User != null && e.User.IsActive)
                .Where(e => !applyOwnerFilter || e.OwnerUserID == ownerUserId)
                .OrderByDescending(e => e.HireDate)
                .Take(4)
                .Select(e => e.FullName)
                .ToListAsync();

            var salesInitials = await _context.Employees
                .Include(e => e.User)
                .Where(e => e.Position == "Sales Staff" && !e.IsArchived && e.User != null && e.User.IsActive)
                .Where(e => !applyOwnerFilter || e.OwnerUserID == ownerUserId)
                .OrderByDescending(e => e.HireDate)
                .Take(4)
                .Select(e => e.FullName)
                .ToListAsync();

            // Fetch Real Activity Logs for Employees only
            var recentLogs = await _context.SystemLogs
                .Include(l => l.User)
                .Where(l => l.User != null && l.User.UserType == "Internal") // Filter for employees only
                .Where(l => l.User != null && (l.User.Role == "Inventory Manager" || l.User.Role == "Sales Staff"))
                .Where(l => !applyOwnerFilter || l.OwnerUserID == ownerUserId)
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .Select(l => new ActivityViewModel
                {
                    Id = l.LogID,
                    UserName = l.User != null ? l.User.FullName : "Unknown",
                    Action = l.Action,
                    Object = "",
                    TimeAgo = GetTimeAgo(l.Timestamp),
                    ActivityType = GetActivityType(l.Action)
                }).ToListAsync();

            var viewModel = new EmployeeManagementViewModel
            {
                OnboardingCount = 0,
                Employees = employees,
                Activities = recentLogs,
                RoleCounts = new Dictionary<string, int>
                {
                    { "Inventory Manager", inventoryCount },
                    { "Sales Staff", salesCount }
                },
                RoleInitials = new Dictionary<string, List<string>>
                {
                    { "Inventory Manager", inventoryInitials.Select(n => GetInitials(n)).ToList() },
                    { "Sales Staff", salesInitials.Select(n => GetInitials(n)).ToList() }
                },
                ShowArchived = normalizedStatus == "archived",
                SelectedStatus = normalizedStatus
            };

            return View(viewModel);
        }

        // Action of Archive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id, string status = "inactive")
        {
            var normalizedStatus = NormalizeStatusFilter(status);
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == id &&
                    !e.IsArchived &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            if (employee == null || employee.User == null)
            {
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            if (employee.User.IsActive)
            {
                TempData["EmployeeError"] = $"{employee.FullName} must be inactive before archiving.";
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            employee.IsArchived = true;
            employee.User.IsActive = false;
            await _context.SaveChangesAsync();

            var currentUserId = _tenantContext.CurrentUserId ?? 0;
            var log = new SystemLog
            {
                UserID = currentUserId != 0 ? currentUserId : employee.UserID,
                OwnerUserID = employee.OwnerUserID ?? employee.User.OwnerUserID,
                Action = $"Archived Employee: {employee.FullName}",
                Timestamp = DateTime.UtcNow
            };
            _context.SystemLogs.Add(log);
            await _context.SaveChangesAsync();

            TempData["EmployeeMessage"] = $"{employee.FullName} has been archived.";
            return RedirectToAction(nameof(Index), new { status = normalizedStatus });
        }

        // Action of Deactivate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id, string status = "active")
        {
            var normalizedStatus = NormalizeStatusFilter(status);
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == id &&
                    !e.IsArchived &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            if (employee == null || employee.User == null)
            {
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            if (employee.User.IsActive)
            {
                employee.User.IsActive = false;
                await _context.SaveChangesAsync();

                var currentUserId = _tenantContext.CurrentUserId ?? 0;
                _context.SystemLogs.Add(new SystemLog
                {
                    UserID = currentUserId != 0 ? currentUserId : employee.UserID,
                    OwnerUserID = employee.OwnerUserID ?? employee.User.OwnerUserID,
                    Action = $"Deactivated Employee: {employee.FullName}",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                TempData["EmployeeMessage"] = $"{employee.FullName} is now inactive.";
            }

            return RedirectToAction(nameof(Index), new { status = normalizedStatus });
        }

        // Action of Reactivate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(int id, string status = "inactive")
        {
            var normalizedStatus = NormalizeStatusFilter(status);
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == id &&
                    !e.IsArchived &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            if (employee == null || employee.User == null)
            {
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            if (!employee.User.IsActive)
            {
                employee.User.IsActive = true;
                await _context.SaveChangesAsync();

                var currentUserId = _tenantContext.CurrentUserId ?? 0;
                _context.SystemLogs.Add(new SystemLog
                {
                    UserID = currentUserId != 0 ? currentUserId : employee.UserID,
                    OwnerUserID = employee.OwnerUserID ?? employee.User.OwnerUserID,
                    Action = $"Reactivated Employee: {employee.FullName}",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                TempData["EmployeeMessage"] = $"{employee.FullName} is active again.";
            }

            return RedirectToAction(nameof(Index), new { status = normalizedStatus });
        }

        // Action of Unarchive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unarchive(int id, string status = "archived")
        {
            var normalizedStatus = NormalizeStatusFilter(status);
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == id &&
                    e.IsArchived &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));

            if (employee == null || employee.User == null)
            {
                return RedirectToAction(nameof(Index), new { status = normalizedStatus });
            }

            employee.IsArchived = false;
            employee.User.IsActive = false;
            await _context.SaveChangesAsync();

            var currentUserId = _tenantContext.CurrentUserId ?? 0;
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = currentUserId != 0 ? currentUserId : employee.UserID,
                OwnerUserID = employee.OwnerUserID ?? employee.User.OwnerUserID,
                Action = $"Unarchived Employee: {employee.FullName}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["EmployeeMessage"] = $"{employee.FullName} moved to inactive.";
            return RedirectToAction(nameof(Index), new { status = normalizedStatus });
        }

        private static string GetInitials(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "?";
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpper();
        }

        // Action of Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateEmployeeViewModel model)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                return RedirectToAction(nameof(Index));
            }

            if (!AllowedEmployeeRoles.Contains(model.Role))
            {
                return RedirectToAction(nameof(Index));
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return Forbid();
            }

            // 1. Check if email exists
            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
            {
                // In a real app we'd show error. For now, just logging/returning
                return RedirectToAction(nameof(Index));
            }

            // 2. Create User
            var user = new User
            {
                Email = model.Email,
                PasswordHash = _authService.HashPassword(model.Password),
                FullName = model.FullName,
                Role = model.Role,
                UserType = "Internal",
                OwnerUserID = ownerUserId,
                IsActive = true,
                DateCreated = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 3. Create Employee Record
            var employee = new Employee
            {
                UserID = user.UserID,
                OwnerUserID = ownerUserId,
                FullName = model.FullName,
                Position = model.Role,
                HireDate = DateTime.UtcNow,
                ContactNumber = "" // Default
            };
            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            // 4. Log Activity
            var currentUserId = _tenantContext.CurrentUserId ?? 0;

            var log = new SystemLog
            {
                UserID = currentUserId != 0 ? currentUserId : user.UserID,
                OwnerUserID = ownerUserId,
                Action = $"Employee Onboarded: {model.FullName} as {model.Role}",
                Timestamp = DateTime.UtcNow
            };

            _context.SystemLogs.Add(log);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // Action of GetEmployee
        [HttpGet]
        public async Task<IActionResult> GetEmployee(int id)
        {
            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == id &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));
            if (employee == null) return NotFound();

            return Json(new EditEmployeeViewModel
            {
                Id = employee.EmpID,
                FullName = employee.FullName,
                Role = employee.Position,
                Email = employee.User?.Email ?? string.Empty,
                ContactNumber = employee.ContactNumber ?? string.Empty,
                HireDate = employee.HireDate,
                IsActive = employee.User?.IsActive ?? false,
                IsArchived = employee.IsArchived
            });
        }

        // Action of Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditEmployeeViewModel model)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["EmployeeError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(model.FullName))
            {
                return RedirectToAction(nameof(Index));
            }

            if (!AllowedEmployeeRoles.Contains(model.Role))
            {
                return RedirectToAction(nameof(Index));
            }

            var fullName = model.FullName.Trim();
            var normalizedContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber)
                ? string.Empty
                : new string(model.ContactNumber.Where(char.IsDigit).Take(15).ToArray());

            var ownerUserId = _tenantContext.OwnerUserId;
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var employee = await _context.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.EmpID == model.Id &&
                    (e.Position == "Inventory Manager" || e.Position == "Sales Staff") &&
                    (!applyOwnerFilter || e.OwnerUserID == ownerUserId));
            if (employee == null) return NotFound();

            employee.FullName = fullName;
            employee.Position = model.Role;
            employee.ContactNumber = normalizedContactNumber;

            // User Update
            if (employee.User != null)
            {
                employee.User.FullName = fullName;
                employee.User.Role = model.Role;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private static EmployeeActivityType GetActivityType(string action)
        {
            if (action.Contains("Sale") || action.Contains("Order")) return EmployeeActivityType.Sale;
            if (action.Contains("Stock") || action.Contains("Inventory")) return EmployeeActivityType.StockUpdate;
            if (action.Contains("Login")) return EmployeeActivityType.Login;
            return EmployeeActivityType.System;
        }

        private static List<string> GetTagsForRole(string role)
        {
            if (role.Contains("Manager")) return new List<string> { "Inventory", "Products", "Procurement", "Settings" };
            if (role.Contains("Sales")) return new List<string> { "CRM", "Marketing", "Inquiries", "Settings" };
            return new List<string> { "Staff" };
        }

        private static string GetTagColorForRole(string role)
        {
            if (role.Contains("Manager")) return "bg-blue-50 text-blue-700";
            if (role.Contains("Sales")) return "bg-green-50 text-green-700";
            return "bg-gray-50 text-gray-700";
        }

        private static string GetTimeAgo(DateTime timestamp)
        {
            var span = DateTime.UtcNow - timestamp;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return timestamp.ToString("MMM dd");
        }

        private static string NormalizeStatusFilter(string? status)
        {
            if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            {
                return "inactive";
            }

            if (string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase))
            {
                return "archived";
            }

            return "active";
        }

        private bool IsSuperAdminScopedReadOnly()
        {
            return _tenantContext.IsSuperAdmin &&
                   (!_tenantContext.HasOwnerScope || !_tenantContext.CanEditOwnerWorkspace);
        }
    }
}


