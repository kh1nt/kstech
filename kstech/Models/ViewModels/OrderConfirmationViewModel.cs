namespace kstech.Models.ViewModels
{
    public class OrderConfirmationViewModel
    {
        public int OrderId { get; set; }
        public DateTime OrderDateUtc { get; set; }
        public decimal TotalAmount { get; set; }
        public string CustomerAddress { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public bool CanProcessPayment { get; set; }
        public bool LoyaltyProgramEnabled { get; set; }
        public decimal LoyaltyOrderSubtotalBeforeDiscount { get; set; }
        public int LoyaltyPointsRedeemed { get; set; }
        public decimal LoyaltyDiscountAmount { get; set; }
        public decimal LoyaltyNetSubtotal { get; set; }
        public decimal LoyaltyBasePointsPerCurrency { get; set; }
        public decimal LoyaltyBasePointsRaw { get; set; }
        public string LoyaltyTierName { get; set; } = string.Empty;
        public decimal LoyaltyTierMultiplier { get; set; }
        public int LoyaltyPointsEarned { get; set; }
        public int LoyaltyNetPointsChange { get; set; }
        public decimal LoyaltyPointValue { get; set; }
    }
}
