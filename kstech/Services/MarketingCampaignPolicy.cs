using System;
using System.Collections.Generic;
using System.Linq;

namespace kstech.Services
{
    public static class MarketingCampaignPolicy
    {
        public const string GeneralAudience = "General";
        public const string GpuOwnersAudience = "GPU Owners";
        public const string NewCustomersAudience = "New Customers";
        public const string VipAudience = "VIP";
        public const string LapsedCustomersAudience = "Lapsed Customers";
        public const string BronzeTierAudience = "Bronze Tier";
        public const string SilverTierAudience = "Silver Tier";
        public const string GoldTierAudience = "Gold Tier";
        public const string PlatinumTierAudience = "Platinum Tier";

        public const string DraftStatus = "Draft";
        public const string ScheduledStatus = "Scheduled";
        public const string CancelledStatus = "Cancelled";
        public const string ProcessingStatus = "Processing";
        public const string CompletedStatus = "Completed";
        public const string CompletedWithErrorsStatus = "CompletedWithErrors";
        public const string NoAudienceStatus = "NoAudience";

        private static readonly IReadOnlyList<string> SupportedAudiences = new[]
        {
            GeneralAudience,
            GpuOwnersAudience,
            NewCustomersAudience,
            VipAudience,
            LapsedCustomersAudience,
            BronzeTierAudience,
            SilverTierAudience,
            GoldTierAudience,
            PlatinumTierAudience
        };

        private static readonly IReadOnlyDictionary<string, string> AudienceDescriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [GeneralAudience] = "Every customer with email enabled.",
                [GpuOwnersAudience] = "Customers who previously purchased GPU products.",
                [NewCustomersAudience] = "Customers registered within the last 30 days.",
                [VipAudience] = "High-value customers based on lifetime spend.",
                [LapsedCustomersAudience] = "Customers inactive for the last 90 days.",
                [BronzeTierAudience] = "Customers currently in Bronze loyalty tier.",
                [SilverTierAudience] = "Customers currently in Silver loyalty tier.",
                [GoldTierAudience] = "Customers currently in Gold loyalty tier.",
                [PlatinumTierAudience] = "Customers currently in Platinum loyalty tier."
            };

        private static readonly IReadOnlyList<string> StatusFilters = new[]
        {
            "All",
            DraftStatus,
            ScheduledStatus,
            CancelledStatus,
            ProcessingStatus,
            CompletedStatus,
            CompletedWithErrorsStatus,
            NoAudienceStatus
        };

        public static IReadOnlyList<string> GetSupportedAudiences()
        {
            return SupportedAudiences;
        }

        public static IReadOnlyList<string> GetStatusFilters()
        {
            return StatusFilters;
        }

        public static bool IsSupportedAudience(string? audience)
        {
            if (string.IsNullOrWhiteSpace(audience))
            {
                return false;
            }

            return SupportedAudiences.Any(option =>
                string.Equals(option, audience.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeAudience(string? audience)
        {
            if (string.IsNullOrWhiteSpace(audience))
            {
                return GeneralAudience;
            }

            var normalized = audience.Trim();
            var match = SupportedAudiences.FirstOrDefault(option =>
                string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase));

            return match ?? GeneralAudience;
        }

        public static bool TryGetTierAudience(string? audience, out string tierName)
        {
            tierName = string.Empty;
            if (string.IsNullOrWhiteSpace(audience))
            {
                return false;
            }

            var normalized = NormalizeAudience(audience);
            if (string.Equals(normalized, BronzeTierAudience, StringComparison.OrdinalIgnoreCase))
            {
                tierName = "Bronze";
                return true;
            }

            if (string.Equals(normalized, SilverTierAudience, StringComparison.OrdinalIgnoreCase))
            {
                tierName = "Silver";
                return true;
            }

            if (string.Equals(normalized, GoldTierAudience, StringComparison.OrdinalIgnoreCase))
            {
                tierName = "Gold";
                return true;
            }

            if (string.Equals(normalized, PlatinumTierAudience, StringComparison.OrdinalIgnoreCase))
            {
                tierName = "Platinum";
                return true;
            }

            return false;
        }

        public static string GetAudienceDescription(string? audience)
        {
            var normalized = NormalizeAudience(audience);
            if (AudienceDescriptions.TryGetValue(normalized, out var description))
            {
                return description;
            }

            return "Customers eligible for email outreach.";
        }

        public static bool CanExecuteStatus(string? status)
        {
            return string.Equals(status, DraftStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, ScheduledStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, CompletedWithErrorsStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, NoAudienceStatus, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanCancelStatus(string? status)
        {
            return string.Equals(status, DraftStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, ScheduledStatus, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsQueuedDeliveryStatus(string? deliveryStatus)
        {
            var normalized = (deliveryStatus ?? string.Empty).Trim();
            return normalized.Equals("Queued", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("request", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("deferred", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAcceptedDeliveryStatus(string? deliveryStatus)
        {
            var normalized = (deliveryStatus ?? string.Empty).Trim();
            return normalized.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Sent", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDeliveredDeliveryStatus(string? deliveryStatus)
        {
            var normalized = (deliveryStatus ?? string.Empty).Trim();
            return normalized.Equals("Delivered", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("opened", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("click", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("unique_opened", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("unique_clicked", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFailedDeliveryStatus(string? deliveryStatus)
        {
            var normalized = (deliveryStatus ?? string.Empty).Trim();
            return normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("hard_bounce", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("soft_bounce", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("blocked", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("invalid", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("error", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("spam", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Unsubscribed", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveCampaignStatusFromDeliveryCounts(
            string? currentStatus,
            int queuedCount,
            int acceptedCount,
            int deliveredCount,
            int failedCount)
        {
            var normalizedCurrent = string.IsNullOrWhiteSpace(currentStatus)
                ? DraftStatus
                : currentStatus.Trim();

            if (queuedCount > 0)
            {
                return ProcessingStatus;
            }

            var terminalCount = acceptedCount + deliveredCount + failedCount;
            if (terminalCount == 0)
            {
                return normalizedCurrent;
            }

            return failedCount > 0 ? CompletedWithErrorsStatus : CompletedStatus;
        }
    }
}
