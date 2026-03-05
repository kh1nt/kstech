using System;
using System.Collections.Generic;

namespace kstech.Models
{
    public class EmployeeViewModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? ImageUrl { get; set; }
        public string? Status { get; set; }
        public bool IsArchived { get; set; }
        public string? ActiveStatusTime { get; set; } // e.g., "Active: 2m ago"
        public bool IsActiveNow { get; set; } // Determines the green dot status
        public List<string> Tags { get; set; } = new(); // e.g., "Reviewer", "Approver", "POS Access"
        public string? TagColorClass { get; set; } // e.g., "bg-blue-100 text-blue-800"
    }

    public class ActivityViewModel
    {
        public int Id { get; set; }
        public string? UserName { get; set; }
        public string? Action { get; set; } // e.g., "added 50 units of..."
        public string? Object { get; set; } // e.g., "Ryzen 9 processors"
        public string? TimeAgo { get; set; } // e.g., "Just now", "42 mins ago"
        public string? Metadata { get; set; } // e.g., "INV-2023-001 • Stock Update"
        public string? Metadata2 { get; set; } // e.g., "₱1,299.00 • Customer: J. Doe"
        public EmployeeActivityType ActivityType { get; set; }
    }

    public enum EmployeeActivityType
    {
        StockUpdate,
        Sale,
        System,
        Issue,
        Login
    }

    public class EmployeeManagementViewModel
    {
        public List<EmployeeViewModel> Employees { get; set; } = new();
        public List<ActivityViewModel> Activities { get; set; } = new();
        public int OnboardingCount { get; set; }
        public Dictionary<string, int> RoleCounts { get; set; } = new();
        public Dictionary<string, List<string>> RoleInitials { get; set; } = new();
        public bool ShowArchived { get; set; } = false;
        public string SelectedStatus { get; set; } = "active";
    }

    public class CreateEmployeeViewModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Sales Staff"; // Default
        public string Password { get; set; } = string.Empty;
    }

    public class EditEmployeeViewModel
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public DateTime HireDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsArchived { get; set; }
    }
}
