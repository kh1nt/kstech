using kstech.Data;
using kstech.Services;
using Microsoft.EntityFrameworkCore;

namespace kstech.Services.Background
{
    public class EmailDeliveryWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EmailDeliveryWorker> _logger;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

        public EmailDeliveryWorker(IServiceProvider serviceProvider, ILogger<EmailDeliveryWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Email Delivery Worker is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExecuteDueScheduledCampaignsAsync(stoppingToken);
                    await ProcessOutboxAsync(stoppingToken);
                    await RefreshProcessingCampaignStatusesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred processing email outbox.");
                }

                await Task.Delay(_pollingInterval, stoppingToken);
            }

            _logger.LogInformation("Email Delivery Worker is stopping.");
        }

        private async Task ExecuteDueScheduledCampaignsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var marketingAutomationService = scope.ServiceProvider.GetRequiredService<IMarketingAutomationService>();
            var nowUtc = DateTime.UtcNow;

            var dueCampaignIds = await dbContext.Campaigns
                .AsNoTracking()
                .Where(campaign =>
                    campaign.ScheduledForUtc.HasValue &&
                    campaign.ScheduledForUtc.Value <= nowUtc &&
                    campaign.Status == MarketingCampaignPolicy.ScheduledStatus)
                .OrderBy(campaign => campaign.ScheduledForUtc)
                .Select(campaign => campaign.CampaignID)
                .Take(10)
                .ToListAsync(stoppingToken);

            foreach (var campaignId in dueCampaignIds)
            {
                try
                {
                    var result = await marketingAutomationService.ExecuteCampaignAsync(
                        campaignId,
                        triggeredByUserId: null,
                        stoppingToken);

                    _logger.LogInformation(
                        "Auto-executed scheduled campaign {CampaignId}. Audience={AudienceSize}, Status={Status}.",
                        campaignId,
                        result.AudienceSize,
                        result.Status);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Scheduled campaign {CampaignId} could not be auto-executed.", campaignId);
                }
            }
        }

        private async Task ProcessOutboxAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var brevoService = scope.ServiceProvider.GetRequiredService<IBrevoEmailService>();

            // Fetch up to 20 unsent emails. RetryCount < 3 means we haven't given up yet.
            var pendingEmails = await dbContext.EmailOutbox
                .Where(e => e.RetryCount < 3)
                .OrderBy(e => e.CreatedAtUtc)
                .Take(20)
                .ToListAsync(stoppingToken);

            if (!pendingEmails.Any())
            {
                return;
            }

            var toDelete = new List<int>();

            foreach (var email in pendingEmails)
            {
                try
                {
                    var result = await brevoService.SendEmailAsync(
                        email.RecipientEmail,
                        email.RecipientEmail,
                        email.Subject,
                        email.HtmlBody,
                        stoppingToken);

                    if (result.Success)
                    {
                        // Update history record if linked
                        if (email.NotifID.HasValue)
                        {
                            var notif = await dbContext.EmailNotifications.FindAsync(new object[] { email.NotifID.Value }, stoppingToken);
                            if (notif != null)
                            {
                                notif.DeliveryStatus = "Accepted";
                                notif.ExternalMessageId = result.MessageId ?? string.Empty;
                            }
                        }

                        toDelete.Add(email.OutboxID);
                        _logger.LogInformation("Successfully sent outbox email {OutboxId}. Removing from outbox.", email.OutboxID);
                    }
                    else
                    {
                        email.RetryCount += 1;
                        email.ErrorMessage = result.ErrorMessage;

                        if (email.RetryCount >= 3)
                        {
                            // Mark history as Failed then delete the outbox entry
                            if (email.NotifID.HasValue)
                            {
                                var notif = await dbContext.EmailNotifications.FindAsync(new object[] { email.NotifID.Value }, stoppingToken);
                                if (notif != null)
                                {
                                    notif.DeliveryStatus = "Failed";
                                }
                            }

                            toDelete.Add(email.OutboxID);
                            _logger.LogError("Permanently failed outbox email {OutboxId} after 3 retries: {Error}. Removing from outbox.", email.OutboxID, result.ErrorMessage);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to send outbox email {OutboxId} (Attempt {Attempt}/3): {Error}", email.OutboxID, email.RetryCount, result.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    email.RetryCount += 1;
                    email.ErrorMessage = ex.Message;

                    if (email.RetryCount >= 3)
                    {
                        if (email.NotifID.HasValue)
                        {
                            var notif = await dbContext.EmailNotifications.FindAsync(new object[] { email.NotifID.Value }, stoppingToken);
                            if (notif != null)
                            {
                                notif.DeliveryStatus = "Failed";
                            }
                        }

                        toDelete.Add(email.OutboxID);
                        _logger.LogError(ex, "Permanently failed outbox email {OutboxId} after 3 retries due to exception. Removing from outbox.", email.OutboxID);
                    }
                    else
                    {
                        _logger.LogError(ex, "Exception while sending outbox email {OutboxId} (Attempt {Attempt}/3)", email.OutboxID, email.RetryCount);
                    }
                }
            }

            // Delete all processed (sent or permanently failed) outbox entries to keep the table clean
            if (toDelete.Count > 0)
            {
                var rowsToDelete = dbContext.EmailOutbox.Where(e => toDelete.Contains(e.OutboxID));
                dbContext.EmailOutbox.RemoveRange(rowsToDelete);
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        private async Task RefreshProcessingCampaignStatusesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var processingCampaignIds = await dbContext.Campaigns
                .AsNoTracking()
                .Where(campaign => campaign.Status == MarketingCampaignPolicy.ProcessingStatus)
                .Select(campaign => campaign.CampaignID)
                .ToListAsync(stoppingToken);

            if (processingCampaignIds.Count == 0)
            {
                return;
            }

            var notificationRows = await dbContext.EmailNotifications
                .AsNoTracking()
                .Where(notification =>
                    notification.CampaignID.HasValue &&
                    processingCampaignIds.Contains(notification.CampaignID.Value))
                .Select(notification => new
                {
                    CampaignID = notification.CampaignID!.Value,
                    notification.DeliveryStatus
                })
                .ToListAsync(stoppingToken);

            // CALC-KPI: Recalculate delivery outcome counts per campaign from raw notification statuses.
            var countsByCampaignId = notificationRows
                .GroupBy(row => row.CampaignID)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        Queued: group.Count(row => MarketingCampaignPolicy.IsQueuedDeliveryStatus(row.DeliveryStatus)),
                        Accepted: group.Count(row => MarketingCampaignPolicy.IsAcceptedDeliveryStatus(row.DeliveryStatus)),
                        Delivered: group.Count(row => MarketingCampaignPolicy.IsDeliveredDeliveryStatus(row.DeliveryStatus)),
                        Failed: group.Count(row => MarketingCampaignPolicy.IsFailedDeliveryStatus(row.DeliveryStatus))));

            var campaigns = await dbContext.Campaigns
                .Where(campaign => processingCampaignIds.Contains(campaign.CampaignID))
                .ToListAsync(stoppingToken);

            var updatedCount = 0;
            foreach (var campaign in campaigns)
            {
                countsByCampaignId.TryGetValue(
                    campaign.CampaignID,
                    out var counts);

                // CALC-RULE: Derive the campaign status transition from the rolled-up delivery counts.
                var nextStatus = MarketingCampaignPolicy.ResolveCampaignStatusFromDeliveryCounts(
                    campaign.Status,
                    counts.Queued,
                    counts.Accepted,
                    counts.Delivered,
                    counts.Failed);

                if (string.Equals(nextStatus, campaign.Status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                campaign.Status = nextStatus;
                updatedCount += 1;
            }

            if (updatedCount <= 0)
            {
                return;
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Updated {UpdatedCount} marketing campaign status values from Processing to terminal states.", updatedCount);
        }
    }
}
