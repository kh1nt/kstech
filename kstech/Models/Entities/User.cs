using System.ComponentModel.DataAnnotations;

namespace kstech.Models.Entities
{
    public class User
    {
        [Key]
        public int UserID { get; set; }

        [Required]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(50)]
        public string Role { get; set; } = "Customer"; // "Owner", "Admin", "Inventory Manager", "Sales Staff", "Customer"

        [StringLength(20)]
        public string UserType { get; set; } = "Customer"; // "Internal", "Customer"

        public int? OwnerUserID { get; set; }

        public bool AllowSuperAdminWorkspaceEdits { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public bool IsEmailVerified { get; set; } = true;

        [StringLength(100)]
        public string? EmailVerificationToken { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEnd { get; set; }

        public DateTime? LastFailedLogin { get; set; }

        public bool TwoFactorEnabled { get; set; } = false;

        public string? TwoFactorSecret { get; set; }

        public string? TwoFactorBackupCodes { get; set; }

        // Navigation Properties
        public Employee? Employee { get; set; }
        public Customer? Customer { get; set; }
        public ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
        public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    }
}
