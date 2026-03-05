using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using kstech.Models;

namespace kstech.Models.Entities
{
    public class Customer
    {
        [Key]
        public int CustomerID { get; set; }

        public int UserID { get; set; }
        [ForeignKey("UserID")]
        public User? User { get; set; }

        [Required]
        [StringLength(50)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [StringLength(20)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(200)]
        public string Address { get; set; } = string.Empty;

        [StringLength(100)]
        public string City { get; set; } = string.Empty;

        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        public bool MarketingOptIn { get; set; } = true;

        [StringLength(100)]
        public string? SteamId { get; set; }
        public int LoyaltyPoints { get; set; }
        public int LifetimePointsEarned { get; set; }
        public int LifetimePointsRedeemed { get; set; }
        public DateTime? LastLoyaltyActivityUtc { get; set; }

        // Navigation Properties
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<TechnicalInquiry> TechnicalInquiries { get; set; } = new List<TechnicalInquiry>();
        public ICollection<EmailNotification> EmailNotifications { get; set; } = new List<EmailNotification>();

    }
}
