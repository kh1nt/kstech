using kstech.Configuration;
using kstech.Models.Entities;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    public interface ILoyaltyService
    {
        LoyaltyTierSnapshot ResolveTier(decimal lifetimeSpend);
        IReadOnlyList<LoyaltyTierSnapshot> GetProgramTiers();
        LoyaltyCheckoutComputation CalculateCheckout(
            Customer customer,
            decimal lifetimeSpendBeforeOrder,
            decimal orderSubtotal,
            int requestedRedeemPoints);
        void ApplyCheckout(Customer customer, Order order, LoyaltyCheckoutComputation computation, DateTime occurredAtUtc);
        decimal EstimateLiability(int points);
        decimal PointValue { get; }
    }

    public sealed record LoyaltyTierSnapshot(
        string Name,
        decimal SpendThreshold,
        decimal EarnMultiplier);

    public sealed record LoyaltyCheckoutComputation(
        int PointsRedeemed,
        decimal RedemptionDiscount,
        int PointsEarned,
        decimal NetSubtotal,
        LoyaltyTierSnapshot Tier);

    public class LoyaltyService : ILoyaltyService
    {
        private readonly LoyaltyProgramOptions _options;

        public LoyaltyService(IOptions<LoyaltyProgramOptions> options)
        {
            _options = options.Value;
        }

        public decimal PointValue => _options.PointRedemptionValue;

        public IReadOnlyList<LoyaltyTierSnapshot> GetProgramTiers()
        {
            return GetTierTable();
        }

        public LoyaltyTierSnapshot ResolveTier(decimal lifetimeSpend)
        {
            var tiers = GetTierTable();
            var tier = tiers
                .Where(t => lifetimeSpend >= t.SpendThreshold)
                .OrderByDescending(t => t.SpendThreshold)
                .FirstOrDefault();

            return tier ?? tiers[0];
        }

        public LoyaltyCheckoutComputation CalculateCheckout(
            Customer customer,
            decimal lifetimeSpendBeforeOrder,
            decimal orderSubtotal,
            int requestedRedeemPoints)
        {
            var sanitizedSubtotal = Math.Max(0m, orderSubtotal);
            var tier = ResolveTier(lifetimeSpendBeforeOrder);

            if (!_options.Enabled || sanitizedSubtotal <= 0m)
            {
                return new LoyaltyCheckoutComputation(
                    PointsRedeemed: 0,
                    RedemptionDiscount: 0m,
                    PointsEarned: 0,
                    NetSubtotal: sanitizedSubtotal,
                    Tier: tier);
            }

            var requested = Math.Max(0, requestedRedeemPoints);
            var redeemablePoints = 0;
            var discountAmount = 0m;

            if (requested > 0 &&
                customer.LoyaltyPoints > 0 &&
                _options.PointRedemptionValue > 0m &&
                sanitizedSubtotal >= _options.MinimumOrderAmountForRedemption)
            {
                var maxDiscountAllowed = sanitizedSubtotal * Math.Clamp(_options.MaxRedemptionRate, 0m, 1m);
                var maxPointsByDiscount = (int)Math.Floor(maxDiscountAllowed / _options.PointRedemptionValue);

                redeemablePoints = Math.Min(requested, customer.LoyaltyPoints);
                redeemablePoints = Math.Min(redeemablePoints, Math.Max(0, maxPointsByDiscount));
                discountAmount = Math.Round(redeemablePoints * _options.PointRedemptionValue, 2, MidpointRounding.AwayFromZero);
            }

            var netSubtotal = Math.Max(0m, sanitizedSubtotal - discountAmount);
            var basePoints = netSubtotal * Math.Max(0m, _options.BasePointsPerCurrency);
            var earnedPoints = (int)Math.Floor(basePoints * Math.Max(0m, tier.EarnMultiplier));

            return new LoyaltyCheckoutComputation(
                PointsRedeemed: redeemablePoints,
                RedemptionDiscount: discountAmount,
                PointsEarned: Math.Max(0, earnedPoints),
                NetSubtotal: netSubtotal,
                Tier: tier);
        }

        public void ApplyCheckout(Customer customer, Order order, LoyaltyCheckoutComputation computation, DateTime occurredAtUtc)
        {
            order.LoyaltyPointsEarned = computation.PointsEarned;
            order.LoyaltyPointsRedeemed = computation.PointsRedeemed;
            order.LoyaltyDiscountAmount = computation.RedemptionDiscount;

            if (computation.PointsRedeemed > 0)
            {
                customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - computation.PointsRedeemed);
                customer.LifetimePointsRedeemed += computation.PointsRedeemed;
                customer.LastLoyaltyActivityUtc = occurredAtUtc;
            }

            if (computation.PointsEarned > 0)
            {
                customer.LoyaltyPoints += computation.PointsEarned;
                customer.LifetimePointsEarned += computation.PointsEarned;
                customer.LastLoyaltyActivityUtc = occurredAtUtc;
            }
        }

        public decimal EstimateLiability(int points)
        {
            if (points <= 0 || _options.PointRedemptionValue <= 0m)
            {
                return 0m;
            }

            return Math.Round(points * _options.PointRedemptionValue, 2, MidpointRounding.AwayFromZero);
        }

        private List<LoyaltyTierSnapshot> GetTierTable()
        {
            return new List<LoyaltyTierSnapshot>
            {
                new("Bronze", 0m, _options.BronzeMultiplier),
                new("Silver", Math.Max(0m, _options.SilverSpendThreshold), _options.SilverMultiplier),
                new("Gold", Math.Max(0m, _options.GoldSpendThreshold), _options.GoldMultiplier),
                new("Platinum", Math.Max(0m, _options.PlatinumSpendThreshold), _options.PlatinumMultiplier)
            };
        }
    }
}
