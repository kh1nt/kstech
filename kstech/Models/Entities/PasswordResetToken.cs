using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class PasswordResetToken
    {
        [Key]
        public int ResetTokenID { get; set; }

        public int UserID { get; set; }

        [ForeignKey(nameof(UserID))]
        public User? User { get; set; }

        [Required]
        [StringLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Audience { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? ConsumedAtUtc { get; set; }
    }
}
