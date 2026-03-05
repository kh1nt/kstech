using System.ComponentModel.DataAnnotations;

namespace kstech.Models.Entities
{
    public class MarketingCampaign
    {
        [Key]
        public int CampaignID { get; set; }

        public int? OwnerUserID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string TargetAudience { get; set; } = "General"; // e.g., "GPU Owners", "New Customers"

        [StringLength(20)]
        public string Status { get; set; } = "Draft"; // Draft, Scheduled, Completed, CompletedWithErrors

        public DateTime? ScheduledForUtc { get; set; }

        public DateTime StartDate { get; set; }
    }
}
