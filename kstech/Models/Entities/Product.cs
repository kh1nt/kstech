using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace kstech.Models.Entities
{
    public class Product
    {
        [Key]
        public int ProductID { get; set; }

        [Required]
        [StringLength(30)]
        public string CategoryName { get; set; } = "Uncategorized";

        public int? OwnerUserID { get; set; }

        [Required]
        [StringLength(50)]
        public string ProductName { get; set; } = string.Empty;

        // Inventory/Pricing
        [Column(TypeName = "decimal(10, 2)")]
        public decimal CostPrice { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal SellingPrice { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal MarketPrice { get; set; }

        [StringLength(50)]
        public string MarketPriceSource { get; set; } = string.Empty;

        public DateTime? LastMarketPriceSyncUtc { get; set; }

        public int StockQuantity { get; set; }

        public int DamagedQuantity { get; set; }

        [StringLength(30)]
        public string ConditionStatus { get; set; } = "Good";

        [StringLength(300)]
        public string ConditionNotes { get; set; } = string.Empty;

        public DateTime? LastConditionCheckUtc { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        // Additional properties from ProductViewModel mapping
        public string Sku { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;

        // Navigation Properties
        public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    }
}
