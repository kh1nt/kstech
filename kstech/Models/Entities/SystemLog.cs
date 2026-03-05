using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using kstech.Models;

namespace kstech.Models.Entities
{
    public class SystemLog
    {
        public const int ActionMaxLength = 100;

        [Key]
        public int LogID { get; set; }

        public int UserID { get; set; }
        [ForeignKey("UserID")]
        public User? User { get; set; }

        public int? OwnerUserID { get; set; }

        private string _action = string.Empty;

        [Required]
        [StringLength(ActionMaxLength)]
        public string Action
        {
            get => _action;
            set => _action = NormalizeAction(value);
        }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        private static string NormalizeAction(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return string.Empty;
            }

            var normalized = action.Trim();
            if (normalized.Length <= ActionMaxLength)
            {
                return normalized;
            }

            const string truncationSuffix = "...";
            var maxPrefixLength = ActionMaxLength - truncationSuffix.Length;
            return normalized[..maxPrefixLength] + truncationSuffix;
        }
    }
}
