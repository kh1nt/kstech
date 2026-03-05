using System.Collections.Generic;

namespace kstech.Models
{
    public class CartViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new List<CartItemViewModel>();
        public bool IsSignedIn { get; set; }
        public int AvailableLoyaltyPoints { get; set; }
        public decimal LoyaltyPointValue { get; set; }
        public int MaxRedeemablePoints { get; set; }
        public decimal MaxLoyaltyDiscount { get; set; }
        public int EstimatedPointsToEarn { get; set; }
        public int ItemCount => Items.Sum(i => i.Quantity);
        public decimal Total => Items.Sum(i => i.Total);
    }

    public class CartItemViewModel
    {
        public int CartItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string? ImageUrl { get; set; }
        public decimal Total => Price * Quantity;
    }
}
