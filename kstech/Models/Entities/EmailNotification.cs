using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class EmailNotification
    {
        [Key]
        public int NotifID { get; set; }

        public int CustomerID { get; set; }
        [ForeignKey("CustomerID")]
        public Customer? Customer { get; set; }

        public int? OwnerUserID { get; set; }

        public int? CampaignID { get; set; }
        [ForeignKey("CampaignID")]
        public MarketingCampaign? Campaign { get; set; }

        [Required]
        [StringLength(50)]
        public string Subject { get; set; } = string.Empty;

        [StringLength(25)]
        public string DeliveryStatus { get; set; } = "Queued";

        [StringLength(100)]
        public string ExternalMessageId { get; set; } = string.Empty;

        public DateTime DateSent { get; set; } = DateTime.UtcNow;
    }
}
