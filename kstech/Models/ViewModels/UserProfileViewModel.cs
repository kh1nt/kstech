using System.ComponentModel.DataAnnotations;

namespace kstech.Models.ViewModels
{
    public class UserProfileViewModel
    {
        public int UserId { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(15)]
        [Display(Name = "Contact Number")]
        public string ContactNumber { get; set; } = string.Empty;

        [Display(Name = "Role")]
        public string Role { get; set; } = string.Empty;

        public bool AllowSuperAdminWorkspaceEdits { get; set; }

        public bool CanConfigureSuperAdminWorkspaceAccess { get; set; }
    }
}
