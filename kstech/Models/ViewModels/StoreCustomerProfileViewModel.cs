using System;
using System.ComponentModel.DataAnnotations;

namespace kstech.Models.ViewModels
{
    public class StoreCustomerProfileViewModel
    {
        [Required]
        [StringLength(50)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(50)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [StringLength(20)]
        [Display(Name = "Phone")]
        public string Phone { get; set; } = string.Empty;

        [StringLength(200)]
        [Display(Name = "Address")]
        public string Address { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "City")]
        public string City { get; set; } = string.Empty;

        [Display(Name = "Marketing emails")]
        public bool MarketingOptIn { get; set; }

        public int LoyaltyPoints { get; set; }
        public int LifetimePointsEarned { get; set; }
        public int LifetimePointsRedeemed { get; set; }
        public int OrderCount { get; set; }
        public decimal LifetimeSpend { get; set; }
        public string CurrentTier { get; set; } = "Bronze";
        public DateTime RegistrationDate { get; set; }

        [StringLength(100, ErrorMessage = "The {0} must be at most {1} characters long.")]
        [Display(Name = "Steam ID (Optional)")]
        public string? SteamId { get; set; }

        public string? SteamPersonaName { get; set; }
        public string? SteamAvatarUrl { get; set; }

        public bool TwoFactorEnabled { get; set; }
        public string? TwoFactorSecret { get; set; }
        public string? TwoFactorQrUrl { get; set; }
    }
}
