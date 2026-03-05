using kstech.Models.ViewModels;
using kstech.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin")]
    public class OwnerController : Controller
    {
        private readonly kstech.Data.ApplicationDbContext _context;
        private readonly kstech.Services.ITenantContext _tenantContext;

        public OwnerController(
            kstech.Data.ApplicationDbContext context,
            kstech.Services.ITenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        // Action of Index
        public IActionResult Index()
        {
            var utcNow = DateTime.UtcNow;
            var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1);

            var owners = _context.Users
                .AsNoTracking()
                .Where(user =>
                    user.UserType == "Internal" &&
                    user.Role == "Owner")
                .Select(owner => new OwnerMonitorItemViewModel
                {
                    UserId = owner.UserID,
                    FullName = owner.FullName,
                    Email = owner.Email,
                    IsActive = owner.IsActive,
                    DateCreated = owner.DateCreated,
                    EmployeeCount = _context.Employees.Count(employee => employee.OwnerUserID == owner.UserID),
                    ProductCount = _context.Products.Count(product =>
                        product.OwnerUserID == owner.UserID &&
                        product.MarketPriceSource != "Archived"),
                    CustomerCount = _context.Orders
                        .Where(order => order.OwnerUserID == owner.UserID)
                        .Select(order => order.CustomerID)
                        .Distinct()
                        .Count(),
                    OrderCount = _context.Orders.Count(order => order.OwnerUserID == owner.UserID),
                    MonthlySales = _context.Orders
                        .Where(order => order.OwnerUserID == owner.UserID && order.OrderDate >= monthStart)
                        .Sum(order => order.TotalAmount),
                    LastActivityUtc = _context.SystemLogs
                        .Where(log => log.OwnerUserID == owner.UserID)
                        .Max(log => (DateTime?)log.Timestamp),
                    AllowSuperAdminWorkspaceEdits = owner.AllowSuperAdminWorkspaceEdits
                })
                .OrderByDescending(owner => owner.MonthlySales)
                .ThenBy(owner => owner.FullName)
                .ToList();

            var selectedOwnerUserId = _tenantContext.OwnerUserId;
            var selectedOwnerName = selectedOwnerUserId.HasValue
                ? owners.FirstOrDefault(owner => owner.UserId == selectedOwnerUserId.Value)?.FullName ?? string.Empty
                : string.Empty;

            var viewModel = new OwnerMonitoringViewModel
            {
                Owners = owners,
                TotalOwners = owners.Count,
                ActiveOwners = owners.Count(owner => owner.IsActive),
                TotalEmployees = owners.Sum(owner => owner.EmployeeCount),
                TotalProducts = owners.Sum(owner => owner.ProductCount),
                MonthlyRevenue = owners.Sum(owner => owner.MonthlySales),
                SelectedOwnerUserId = selectedOwnerUserId,
                SelectedOwnerName = selectedOwnerName
            };

            return View(viewModel);
        }

        // Action of SelectOwnerScope
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SelectOwnerScope(
            int ownerUserId,
            string redirectController = "Home",
            string redirectAction = "Index")
        {
            var owner = _context.Users
                .AsNoTracking()
                .FirstOrDefault(user =>
                    user.UserID == ownerUserId &&
                    user.UserType == "Internal" &&
                    user.Role == "Owner");

            if (owner == null)
            {
                TempData["OwnerScopeError"] = "Selected owner account was not found.";
                return RedirectToAction(nameof(Index));
            }

            var scopeApplied = _tenantContext.SetSuperAdminOwnerScope(ownerUserId);
            if (!scopeApplied)
            {
                return Forbid();
            }

            AddScopeAuditLog(
                ownerUserId,
                $"SuperAdmin entered owner workspace: {owner.FullName} ({owner.Email}).");

            return RedirectToAction(redirectAction, redirectController);
        }

        // Action of ClearOwnerScope
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ClearOwnerScope(
            string redirectController = "Owner",
            string redirectAction = "Index")
        {
            var previouslySelectedOwnerUserId = _tenantContext.OwnerUserId;
            if (previouslySelectedOwnerUserId.HasValue)
            {
                AddScopeAuditLog(
                    previouslySelectedOwnerUserId.Value,
                    "SuperAdmin returned to global workspace view.");
            }

            _tenantContext.ClearSuperAdminOwnerScope();
            return RedirectToAction(redirectAction, redirectController);
        }

        private void AddScopeAuditLog(int ownerUserId, string action)
        {
            var actorUserId = _tenantContext.CurrentUserId ?? ownerUserId;
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId,
                OwnerUserID = ownerUserId,
                Action = action,
                Timestamp = DateTime.UtcNow
            });

            _context.SaveChanges();
        }
    }
}
