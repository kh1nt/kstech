using System;
using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class StoreOrderHistoryViewModel
    {
        public int TotalOrders { get; set; }
        public int TotalItemsPurchased { get; set; }
        public decimal LifetimeSpend { get; set; }
        public List<StoreOrderHistoryItemViewModel> Orders { get; set; } = new();
    }

    public class StoreOrderHistoryItemViewModel
    {
        public int OrderId { get; set; }
        public DateTime OrderedAtUtc { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public string OrderStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public int LoyaltyPointsEarned { get; set; }
        public int LoyaltyPointsRedeemed { get; set; }
    }
}
