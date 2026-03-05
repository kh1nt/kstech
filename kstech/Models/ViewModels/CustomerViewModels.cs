using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace kstech.Models
{
    public class CustomerManagementViewModel
    {
        public List<CustomerViewModel> Customers { get; set; } = new List<CustomerViewModel>();
        public List<LoyaltyTierStatViewModel> LoyaltyTierStats { get; set; } = new List<LoyaltyTierStatViewModel>();
        public List<LoyaltyTierDetailViewModel> LoyaltyTierDetails { get; set; } = new List<LoyaltyTierDetailViewModel>();
        public List<string> LoyaltyPointBucketLabels { get; set; } = new List<string>();
        public List<int> LoyaltyPointBucketValues { get; set; } = new List<int>();
        public int TotalOutstandingPoints { get; set; }
        public decimal LoyaltyLiability { get; set; }
        public Dictionary<int, List<CustomerOutreachMessageViewModel>> OutreachHistoryByCustomer { get; set; } = new Dictionary<int, List<CustomerOutreachMessageViewModel>>();
    }

    public class CustomerViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Level { get; set; } = "Bronze"; // Platinum, Gold, Silver, Bronze
        public string LevelColorClass { get; set; } = "bg-amber-100 text-amber-700 border-amber-200";
        public decimal TotalSpent { get; set; }
        public int LoyaltyPoints { get; set; }
        public int LifetimePointsEarned { get; set; }
        public int LifetimePointsRedeemed { get; set; }
        public decimal LifetimeRewardsValue { get; set; }
        public int OrderCount { get; set; }
        public bool MarketingOptIn { get; set; } = true;
        public DateTime RegistrationDate { get; set; }
    }

    public class LoyaltyTierDetailViewModel
    {
        public string Tier { get; set; } = string.Empty;
        public decimal SpendThreshold { get; set; }
        public decimal EarnMultiplier { get; set; }
        public int Members { get; set; }
        public string ColorClass { get; set; } = string.Empty;
        public List<string> Benefits { get; set; } = new List<string>();
    }

    public class LoyaltyTierStatViewModel
    {
        public string Tier { get; set; } = string.Empty;
        public int Members { get; set; }
    }

    public class CustomerOutreachMessageViewModel
    {
        public string Subject { get; set; } = string.Empty;
        public string DeliveryStatus { get; set; } = string.Empty;
        public string Source { get; set; } = "Quick Email";
        public DateTime DateSent { get; set; }
        public string? CampaignName { get; set; }
        public bool IsCampaign { get; set; }
    }

    public class QuickMessageInputViewModel
    {
        public const int MaxSubjectLength = 50;
        public const int MaxMessageLength = 320;

        [Required]
        public int CustomerId { get; set; }

        [Required]
        [StringLength(MaxSubjectLength)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [StringLength(MaxMessageLength)]
        public string Message { get; set; } = string.Empty;
    }
}
