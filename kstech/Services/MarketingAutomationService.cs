using kstech.Data;
using kstech.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace kstech.Services
{
    public record CampaignSendResult(int AudienceSize, int QueuedCount, int FailedCount, string Status);

    public interface IMarketingAutomationService
    {
        Task<CampaignSendResult> ExecuteCampaignAsync(
            int campaignId,
            int? triggeredByUserId,
            CancellationToken cancellationToken = default);

        Task<string> CancelCampaignAsync(
            int campaignId,
            int? triggeredByUserId,
            CancellationToken cancellationToken = default);
    }

    public class MarketingAutomationService : IMarketingAutomationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IEmailOutboxService _emailOutboxService;
        private readonly ILoyaltyService _loyaltyService;
        private readonly ILogger<MarketingAutomationService> _logger;

        public MarketingAutomationService(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            IEmailOutboxService emailOutboxService,
            ILoyaltyService loyaltyService,
            ILogger<MarketingAutomationService> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _emailOutboxService = emailOutboxService;
            _loyaltyService = loyaltyService;
            _logger = logger;
        }

        public async Task<CampaignSendResult> ExecuteCampaignAsync(
            int campaignId,
            int? triggeredByUserId,
            CancellationToken cancellationToken = default)
        {
            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                throw new InvalidOperationException(
                    "This owner workspace is read-only for SuperAdmin. Owner approval is required for edit actions.");
            }

            var campaignQuery = _context.Campaigns
                .Where(campaignRow => campaignRow.CampaignID == campaignId)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                campaignQuery = campaignQuery.Where(campaignRow => campaignRow.OwnerUserID == ownerUserId);
            }

            var campaign = await campaignQuery
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Campaign {campaignId} was not found.");

            campaign.TargetAudience = MarketingCampaignPolicy.NormalizeAudience(campaign.TargetAudience);

            if (!MarketingCampaignPolicy.CanExecuteStatus(campaign.Status))
            {
                throw new InvalidOperationException(
                    $"Campaign '{campaign.Name}' is in '{campaign.Status}' status and cannot be executed.");
            }

            var targetCustomers = await GetAudienceAsync(
                campaign.TargetAudience,
                campaign.OwnerUserID,
                cancellationToken);
            // CALC-SEGMENT: Deduplicate by normalized email so one campaign does not queue duplicate sends.
            targetCustomers = targetCustomers
                .Where(customer => !string.IsNullOrWhiteSpace(customer.Email))
                .GroupBy(customer => customer.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (targetCustomers.Count == 0)
            {
                campaign.Status = MarketingCampaignPolicy.NoAudienceStatus;
                campaign.ScheduledForUtc = null;
                campaign.StartDate = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Campaign {CampaignId} executed by user {TriggeredByUserId} but no email-enabled audience matched {TargetAudience}.",
                    campaign.CampaignID,
                    triggeredByUserId,
                    campaign.TargetAudience);

                return new CampaignSendResult(0, 0, 0, campaign.Status);
            }

            var queuedCount = 0;
            var sentAtUtc = DateTime.UtcNow;

            foreach (var customer in targetCustomers)
            {
                var subject = campaign.Name;
                var messageHtml = BuildEmailBody(campaign, customer);

                // Save the history record first so we get its ID to link the outbox entry
                var notification = new EmailNotification
                {
                    CustomerID = customer.CustomerID,
                    OwnerUserID = campaign.OwnerUserID,
                    CampaignID = campaign.CampaignID,
                    Subject = subject,
                    DeliveryStatus = "Queued",
                    ExternalMessageId = string.Empty,
                    DateSent = sentAtUtc
                };
                _context.EmailNotifications.Add(notification);
                await _context.SaveChangesAsync(cancellationToken);

                await _emailOutboxService.QueueEmailAsync(
                    customer.Email,
                    subject,
                    messageHtml,
                    campaign.OwnerUserID,
                    notifId: notification.NotifID);

                queuedCount += 1;
            }

            var status = MarketingCampaignPolicy.ProcessingStatus;
            campaign.Status = status;
            campaign.ScheduledForUtc = null;
            campaign.StartDate = sentAtUtc;

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Campaign {CampaignId} queued by user {TriggeredByUserId}. Status={Status}, Audience={AudienceSize}, Queued={QueuedCount}.",
                campaign.CampaignID,
                triggeredByUserId,
                status,
                targetCustomers.Count,
                queuedCount);

            return new CampaignSendResult(
                targetCustomers.Count,
                queuedCount,
                0,
                status);
        }

        public async Task<string> CancelCampaignAsync(
            int campaignId,
            int? triggeredByUserId,
            CancellationToken cancellationToken = default)
        {
            if (_tenantContext.IsSuperAdmin &&
                _tenantContext.HasOwnerScope &&
                !_tenantContext.CanEditOwnerWorkspace)
            {
                throw new InvalidOperationException(
                    "This owner workspace is read-only for SuperAdmin. Owner approval is required for edit actions.");
            }

            var campaignQuery = _context.Campaigns
                .Where(campaignRow => campaignRow.CampaignID == campaignId)
                .AsQueryable();

            if (_tenantContext.HasOwnerScope)
            {
                var ownerUserId = _tenantContext.OwnerUserId ?? 0;
                campaignQuery = campaignQuery.Where(campaignRow => campaignRow.OwnerUserID == ownerUserId);
            }

            var campaign = await campaignQuery
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Campaign {campaignId} was not found.");

            if (!MarketingCampaignPolicy.CanCancelStatus(campaign.Status))
            {
                throw new InvalidOperationException(
                    $"Campaign '{campaign.Name}' is in '{campaign.Status}' status and cannot be cancelled.");
            }

            campaign.Status = MarketingCampaignPolicy.CancelledStatus;
            campaign.ScheduledForUtc = null;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Campaign {CampaignId} cancelled by user {TriggeredByUserId}.",
                campaign.CampaignID,
                triggeredByUserId);

            return campaign.Name;
        }

        private async Task<List<Customer>> GetAudienceAsync(
            string targetAudience,
            int? campaignOwnerUserId,
            CancellationToken cancellationToken)
        {
            var normalized = MarketingCampaignPolicy.NormalizeAudience(targetAudience);
            var applyOwnerFilter = campaignOwnerUserId.HasValue;
            var ownerUserId = campaignOwnerUserId ?? 0;
            var customers = _context.Customers
                .AsNoTracking()
                .Where(customer => customer.MarketingOptIn && !string.IsNullOrWhiteSpace(customer.Email))
                .AsQueryable();

            if (applyOwnerFilter)
            {
                customers = customers.Where(customer =>
                    customer.Orders.Any(order => order.OwnerUserID == ownerUserId));
            }

            if (MarketingCampaignPolicy.TryGetTierAudience(normalized, out var tierName))
            {
                // CALC-SEGMENT: Resolve loyalty-tier audience membership from lifetime spend totals.
                var spendByCustomer = await _context.Orders
                    .AsNoTracking()
                    .Where(order => !applyOwnerFilter || order.OwnerUserID == ownerUserId)
                    .GroupBy(order => order.CustomerID)
                    .Select(group => new
                    {
                        CustomerID = group.Key,
                        TotalSpent = group.Sum(order => order.TotalAmount)
                    })
                    .ToDictionaryAsync(
                        row => row.CustomerID,
                        row => row.TotalSpent,
                        cancellationToken);

                var eligibleCustomers = await customers.ToListAsync(cancellationToken);
                return eligibleCustomers
                    .Where(customer =>
                    {
                        var totalSpent = spendByCustomer.TryGetValue(customer.CustomerID, out var spent) ? spent : 0m;
                        var resolvedTier = _loyaltyService.ResolveTier(totalSpent).Name;
                        return string.Equals(resolvedTier, tierName, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            if (string.Equals(normalized, MarketingCampaignPolicy.NewCustomersAudience, StringComparison.OrdinalIgnoreCase))
            {
                // CALC-SEGMENT: New-customer audience uses a rolling 30-day registration cutoff.
                var cutoff = DateTime.UtcNow.AddDays(-30);
                return await customers
                    .Where(customer => customer.RegistrationDate >= cutoff)
                    .ToListAsync(cancellationToken);
            }

            if (string.Equals(normalized, MarketingCampaignPolicy.VipAudience, StringComparison.OrdinalIgnoreCase))
            {
                // CALC-SEGMENT: VIP audience uses the lifetime spend threshold (>= 100,000).
                var vipCustomers = await _context.Orders
                    .AsNoTracking()
                    .Where(order => !applyOwnerFilter || order.OwnerUserID == ownerUserId)
                    .GroupBy(order => order.CustomerID)
                    .Select(group => new { CustomerID = group.Key, TotalSpent = group.Sum(order => order.TotalAmount) })
                    .Where(group => group.TotalSpent >= 100000m)
                    .Select(group => group.CustomerID)
                    .ToListAsync(cancellationToken);

                return await customers
                    .Where(customer => vipCustomers.Contains(customer.CustomerID))
                    .ToListAsync(cancellationToken);
            }

            if (string.Equals(normalized, MarketingCampaignPolicy.GpuOwnersAudience, StringComparison.OrdinalIgnoreCase))
            {
                // CALC-SEGMENT: GPU-owner audience is inferred from historical GPU order details.
                var gpuOwners = await _context.OrderDetails
                    .AsNoTracking()
                    .Where(detail => detail.Order != null &&
                                     (!applyOwnerFilter || detail.Order.OwnerUserID == ownerUserId) &&
                                     detail.Product != null &&
                                     detail.Product.CategoryName == "GPU")
                    .Select(detail => detail.Order!.CustomerID)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                return await customers
                    .Where(customer => gpuOwners.Contains(customer.CustomerID))
                    .ToListAsync(cancellationToken);
            }

            if (string.Equals(normalized, MarketingCampaignPolicy.LapsedCustomersAudience, StringComparison.OrdinalIgnoreCase))
            {
                // CALC-SEGMENT: Lapsed customers are older accounts without recent orders in the last 90 days.
                var cutoff = DateTime.UtcNow.AddDays(-90);
                var recentlyActiveCustomerIds = await _context.Orders
                    .AsNoTracking()
                    .Where(order => !applyOwnerFilter || order.OwnerUserID == ownerUserId)
                    .Where(order => order.OrderDate >= cutoff)
                    .Select(order => order.CustomerID)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                return await customers
                    .Where(customer => customer.RegistrationDate < cutoff)
                    .Where(customer => !recentlyActiveCustomerIds.Contains(customer.CustomerID))
                    .ToListAsync(cancellationToken);
            }

            return await customers.ToListAsync(cancellationToken);
        }

        private static string BuildEmailBody(MarketingCampaign campaign, Customer customer)
        {
            var safeName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Customer"
                : customer.FullName;

            return $"""
                <div style="font-family:Arial,sans-serif;line-height:1.5;">
                  <h2>{campaign.Name}</h2>
                  <p>Hello {safeName},</p>
                  <p>{campaign.Description ?? string.Empty}</p>
                  <p>Thank you for choosing KSTech.</p>
                </div>
                """;
        }
    }
}
