namespace kstech.Models.ViewModels
{
    public class OwnerMonitoringViewModel
    {
        public List<OwnerMonitorItemViewModel> Owners { get; set; } = new();
        public int TotalOwners { get; set; }
        public int ActiveOwners { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalProducts { get; set; }
        public decimal MonthlyRevenue { get; set; }
        public int? SelectedOwnerUserId { get; set; }
        public string SelectedOwnerName { get; set; } = string.Empty;
    }

    public class OwnerMonitorItemViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime DateCreated { get; set; }
        public int EmployeeCount { get; set; }
        public int ProductCount { get; set; }
        public int CustomerCount { get; set; }
        public int OrderCount { get; set; }
        public decimal MonthlySales { get; set; }
        public DateTime? LastActivityUtc { get; set; }
        public bool AllowSuperAdminWorkspaceEdits { get; set; }
    }
}
