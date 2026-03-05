using System.ComponentModel.DataAnnotations;

namespace kstech.Models.Entities
{
    public class EmailOutbox
    {
        [Key]
        public int OutboxID { get; set; }

        public int? OwnerUserID { get; set; }

        /// <summary>
        /// Optional link to the EmailNotification history record so the worker
        /// can update DeliveryStatus once the email is sent or permanently failed.
        /// </summary>
        public int? NotifID { get; set; }

        [Required]
        [StringLength(255)]
        public string RecipientEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string HtmlBody { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public int RetryCount { get; set; } = 0;

        [StringLength(500)]
        public string? ErrorMessage { get; set; }
    }
}
