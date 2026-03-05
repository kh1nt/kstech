using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class TechnicalInquiry
    {
        [Key]
        public int InquiryID { get; set; }

        public int CustomerID { get; set; }
        [ForeignKey("CustomerID")]
        public Customer? Customer { get; set; }

        public int? OwnerUserID { get; set; }

        [Required]
        [StringLength(100)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string InquiryMessage { get; set; } = string.Empty;

        public DateTime DateSubmittedUtc { get; set; } = DateTime.UtcNow;

        public bool IsResolved { get; set; } = false;
        
        public DateTime? DateResolvedUtc { get; set; }

        public string? ResolutionNotes { get; set; }
    }
}
