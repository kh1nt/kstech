using System.Globalization;
using System.Security.Claims;
using kstech.Data;
using kstech.Filters;
using kstech.Models;
using kstech.Models.Entities;
using kstech.Services;
using kstech.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kstech.Controllers
{
    [Authorize(AuthenticationSchemes = "AdminScheme", Roles = "SuperAdmin,Owner,Sales Staff")]
    [RequireOwnerScopeForSuperAdmin]
    public class CRMController : Controller
    {
        private const int MaxOutreachHistoryItems = 8;

        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IMarketingAutomationService _marketingAutomationService;
        private readonly ILoyaltyService _loyaltyService;
        private readonly IEmailOutboxService _emailOutboxService;
        private readonly IInventoryService _inventoryService;

        public CRMController(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            IMarketingAutomationService marketingAutomationService,
            ILoyaltyService loyaltyService,
            IEmailOutboxService emailOutboxService,
            IInventoryService inventoryService)
        {
            _context = context;
            _tenantContext = tenantContext;
            _marketingAutomationService = marketingAutomationService;
            _loyaltyService = loyaltyService;
            _emailOutboxService = emailOutboxService;
            _inventoryService = inventoryService;
        }

        // Action of Index
        public IActionResult Index()
        {
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var customerRows = _context.Customers
                .AsNoTracking()
                .Where(customer =>
                    !applyOwnerFilter ||
                    customer.Orders.Any(order => order.OwnerUserID == ownerUserId))
                .Select(c => new
                {
                    c.CustomerID,
                    c.FullName,
                    c.Email,
                    c.Phone,
                    c.MarketingOptIn,
                    c.RegistrationDate,
                    c.LoyaltyPoints,
                    c.LifetimePointsEarned,
                    c.LifetimePointsRedeemed,
                    OrderCount = c.Orders.Count(order =>
                        !applyOwnerFilter || order.OwnerUserID == ownerUserId),
                    TotalSpent = c.Orders
                        .Where(order => !applyOwnerFilter || order.OwnerUserID == ownerUserId)
                        .Sum(order => (decimal?)order.TotalAmount) ?? 0m
                })
                .ToList();

            var customers = customerRows
                .Select(c =>
                {
                    var tier = _loyaltyService.ResolveTier(c.TotalSpent);
                    return new CustomerViewModel
                    {
                        Id = c.CustomerID,
                        Name = c.FullName,
                        Email = c.Email,
                        Phone = c.Phone,
                        MarketingOptIn = c.MarketingOptIn,
                        RegistrationDate = c.RegistrationDate,
                        OrderCount = c.OrderCount,
                        TotalSpent = c.TotalSpent,
                        Level = tier.Name,
                        LevelColorClass = GetLevelColorClass(tier.Name),
                        LoyaltyPoints = c.LoyaltyPoints,
                        LifetimePointsEarned = c.LifetimePointsEarned,
                        LifetimePointsRedeemed = c.LifetimePointsRedeemed,
                        LifetimeRewardsValue = _loyaltyService.EstimateLiability(c.LifetimePointsRedeemed)
                    };
                })
                .OrderByDescending(c => c.TotalSpent)
                .ToList();

            var customerIds = customers.Select(customer => customer.Id).ToList();
            var outreachRows = _context.EmailNotifications
                .AsNoTracking()
                .Where(notification => customerIds.Contains(notification.CustomerID))
                .Where(notification => !applyOwnerFilter || notification.OwnerUserID == ownerUserId)
                .OrderByDescending(notification => notification.DateSent)
                .Select(notification => new
                {
                    notification.CustomerID,
                    notification.Subject,
                    notification.DeliveryStatus,
                    notification.DateSent,
                    notification.CampaignID,
                    CampaignName = notification.Campaign != null ? notification.Campaign.Name : null
                })
                .ToList();

            var outreachHistoryByCustomer = outreachRows
                .GroupBy(row => row.CustomerID)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(row => row.DateSent)
                        .Take(MaxOutreachHistoryItems)
                        .Select(row => new CustomerOutreachMessageViewModel
                        {
                            Subject = row.Subject,
                            DeliveryStatus = row.DeliveryStatus,
                            DateSent = row.DateSent,
                            CampaignName = row.CampaignName,
                            IsCampaign = row.CampaignID.HasValue,
                            Source = row.CampaignID.HasValue ? "Campaign" : "Quick Email"
                        })
                        .ToList());

            var tierBenefits = BuildTierBenefits();
            var tierDetails = _loyaltyService.GetProgramTiers()
                .OrderByDescending(tier => tier.SpendThreshold)
                .Select(tier => new LoyaltyTierDetailViewModel
                {
                    Tier = tier.Name,
                    SpendThreshold = tier.SpendThreshold,
                    EarnMultiplier = tier.EarnMultiplier,
                    Members = customers.Count(customer => string.Equals(customer.Level, tier.Name, StringComparison.OrdinalIgnoreCase)),
                    ColorClass = GetLevelColorClass(tier.Name),
                    Benefits = tierBenefits.TryGetValue(tier.Name, out var benefits)
                        ? benefits
                        : new List<string> { "Standard point accrual." }
                })
                .ToList();

            var tierStats = tierDetails
                .Select(detail => new LoyaltyTierStatViewModel
                {
                    Tier = detail.Tier,
                    Members = detail.Members
                })
                .ToList();

            var pointBuckets = BuildLoyaltyPointBuckets(customers);
            var totalOutstandingPoints = customers.Sum(customer => customer.LoyaltyPoints);

            var viewModel = new CustomerManagementViewModel
            {
                Customers = customers,
                LoyaltyTierStats = tierStats,
                LoyaltyTierDetails = tierDetails,
                LoyaltyPointBucketLabels = pointBuckets.Select(bucket => bucket.Label).ToList(),
                LoyaltyPointBucketValues = pointBuckets.Select(bucket => bucket.Count).ToList(),
                TotalOutstandingPoints = totalOutstandingPoints,
                LoyaltyLiability = _loyaltyService.EstimateLiability(totalOutstandingPoints),
                OutreachHistoryByCustomer = outreachHistoryByCustomer
            };

            return View(viewModel);
        }

        private static List<(string Label, int Count)> BuildLoyaltyPointBuckets(IEnumerable<CustomerViewModel> customers)
        {
            return new List<(string Label, int Count)>
            {
                ("0-99", customers.Count(c => c.LoyaltyPoints < 100)),
                ("100-499", customers.Count(c => c.LoyaltyPoints >= 100 && c.LoyaltyPoints <= 499)),
                ("500-1999", customers.Count(c => c.LoyaltyPoints >= 500 && c.LoyaltyPoints <= 1999)),
                ("2000+", customers.Count(c => c.LoyaltyPoints >= 2000))
            };
        }

        private static Dictionary<string, List<string>> BuildTierBenefits()
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = new List<string>
                {
                    "1.5x point multiplier on every purchase.",
                    "Priority support and restock alerts.",
                    "Early access to high-demand drops."
                },
                ["Gold"] = new List<string>
                {
                    "1.25x point multiplier on every purchase.",
                    "Priority queue for support requests.",
                    "Member-only accessory promos."
                },
                ["Silver"] = new List<string>
                {
                    "1.1x point multiplier on every purchase.",
                    "Monthly loyalty offers."
                },
                ["Bronze"] = new List<string>
                {
                    "Base points accrual on all eligible purchases."
                }
            };
        }

        private static string GetLevelColorClass(string level)
        {
            switch (level)
            {
                case "Platinum": return "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300 border-blue-200 dark:border-blue-800";
                case "Gold": return "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300 border-yellow-200 dark:border-yellow-800";
                case "Silver": return "bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 border-gray-200 dark:border-gray-700";
                default: return "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300 border-amber-200 dark:border-amber-800";
            }
        }

        // Action of Marketing
        public IActionResult Marketing(
            string? search,
            string? status,
            string? audience,
            int page = 1,
            int pageSize = 10,
            bool openCreateModal = false)
        {
            var viewModel = BuildMarketingViewModel(
                search,
                status,
                audience,
                page,
                pageSize,
                createCampaignForm: null,
                createCampaignErrors: null,
                openCreateCampaignModal: openCreateModal);

            return View(viewModel);
        }

        // Action of CreateCampaign
        [HttpGet]
        public IActionResult CreateCampaign()
        {
            return RedirectToAction(nameof(Marketing), new { openCreateModal = true });
        }

        // Action of CreateCampaign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateCampaign(
            MarketingCampaignCreateInputViewModel request,
            string? search,
            string? status,
            string? audience,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CampaignError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            var createRequest = new MarketingCampaignCreateInputViewModel
            {
                Name = request.Name?.Trim() ?? string.Empty,
                Description = request.Description?.Trim() ?? string.Empty,
                TargetAudience = request.TargetAudience?.Trim() ?? string.Empty,
                ScheduledExecutionLocal = request.ScheduledExecutionLocal?.Trim() ?? string.Empty
            };
            var errors = new List<string>();
            DateTime? scheduledForUtc = null;

            if (string.IsNullOrWhiteSpace(createRequest.Name))
            {
                errors.Add("Campaign name is required.");
            }
            else if (createRequest.Name.Length > 100)
            {
                errors.Add("Campaign name cannot exceed 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(createRequest.Description))
            {
                errors.Add("Message is required for campaign emailing.");
            }
            else if (createRequest.Description.Length > 255)
            {
                errors.Add("Message cannot exceed 255 characters.");
            }

            if (!MarketingCampaignPolicy.IsSupportedAudience(createRequest.TargetAudience))
            {
                errors.Add("Select a valid target audience.");
                createRequest.TargetAudience = MarketingCampaignPolicy.GeneralAudience;
            }
            else
            {
                createRequest.TargetAudience = MarketingCampaignPolicy.NormalizeAudience(createRequest.TargetAudience);
            }

            if (!string.IsNullOrWhiteSpace(createRequest.ScheduledExecutionLocal))
            {
                if (!TryParseBusinessDateTimeLocalInput(createRequest.ScheduledExecutionLocal, out var scheduledExecutionLocal))
                {
                    errors.Add("Enter a valid schedule date and time.");
                }
                else
                {
                    scheduledForUtc = BusinessTime.ConvertBusinessDateTimeToUtc(scheduledExecutionLocal);
                    if (scheduledForUtc.Value < DateTime.UtcNow.AddMinutes(-1))
                    {
                        errors.Add("Schedule date/time must be in the future.");
                    }

                    createRequest.ScheduledExecutionLocal = scheduledExecutionLocal.ToString(
                        "yyyy-MM-ddTHH:mm",
                        CultureInfo.InvariantCulture);
                }
            }

            var ownerUserId = _tenantContext.OwnerUserId;
            if (!ownerUserId.HasValue)
            {
                return Forbid();
            }

            var duplicateCampaignExists = _context.Campaigns
                .AsNoTracking()
                .Where(existing => existing.OwnerUserID == ownerUserId.Value)
                .Where(existing => existing.Name == createRequest.Name)
                .Where(existing => existing.TargetAudience == createRequest.TargetAudience)
                .Where(existing =>
                    existing.Status == MarketingCampaignPolicy.DraftStatus ||
                    existing.Status == MarketingCampaignPolicy.ScheduledStatus)
                .Any(existing => existing.StartDate >= DateTime.UtcNow.AddHours(-24));

            if (duplicateCampaignExists)
            {
                errors.Add("A similar draft or scheduled campaign already exists in the last 24 hours.");
            }

            if (errors.Count > 0)
            {
                var errorViewModel = BuildMarketingViewModel(
                    search,
                    status,
                    audience,
                    page,
                    pageSize,
                    createCampaignForm: createRequest,
                    createCampaignErrors: errors,
                    openCreateCampaignModal: true);
                return View("Marketing", errorViewModel);
            }

            var campaign = new MarketingCampaign
            {
                Name = createRequest.Name,
                Description = createRequest.Description,
                TargetAudience = createRequest.TargetAudience,
                ScheduledForUtc = scheduledForUtc
            };
            campaign.OwnerUserID = ownerUserId;
            campaign.StartDate = DateTime.UtcNow;
            campaign.Status = scheduledForUtc.HasValue
                ? MarketingCampaignPolicy.ScheduledStatus
                : MarketingCampaignPolicy.DraftStatus;
            _context.Campaigns.Add(campaign);

            var actorUserId = TryGetCurrentUserId();
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId ?? 1,
                OwnerUserID = ownerUserId,
                Action = BuildSystemLogAction($"Created campaign {campaign.Name}"),
                Timestamp = DateTime.UtcNow
            });
            _context.SaveChanges();

            TempData["CampaignMessage"] = scheduledForUtc.HasValue
                ? $"Campaign '{campaign.Name}' is scheduled for {BusinessTime.ConvertUtcToBusinessTime(scheduledForUtc.Value):MMM dd, yyyy HH:mm}."
                : $"Campaign '{campaign.Name}' was created as Draft. Use Execute to run it any time.";
            return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
        }

        // Action of ExecuteCampaign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExecuteCampaign(
            int id,
            string? search = null,
            string? status = null,
            string? audience = null,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CampaignError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            if (id <= 0)
            {
                TempData["CampaignError"] = "Invalid campaign identifier.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            try
            {
                var actorUserId = TryGetCurrentUserId();
                var execution = await _marketingAutomationService.ExecuteCampaignAsync(id, actorUserId);

                TempData["CampaignMessage"] = execution.AudienceSize == 0
                    ? "Campaign executed with no eligible email-enabled recipients. Status was updated to NoAudience."
                    : $"Campaign executed. Queued: {execution.QueuedCount}, Failed: {execution.FailedCount}. Delivery status will update shortly.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["CampaignError"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["CampaignError"] = $"Campaign execution failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelCampaign(
            int id,
            string? search = null,
            string? status = null,
            string? audience = null,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CampaignError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            if (id <= 0)
            {
                TempData["CampaignError"] = "Invalid campaign identifier.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            try
            {
                var actorUserId = TryGetCurrentUserId();
                var campaignName = await _marketingAutomationService.CancelCampaignAsync(id, actorUserId);

                TempData["CampaignMessage"] = $"Campaign '{campaignName}' was cancelled.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["CampaignError"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["CampaignError"] = $"Campaign cancellation failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult QueueSlowMoverPromotion(
            int productId,
            string? search = null,
            string? status = null,
            string? audience = null,
            int page = 1,
            int pageSize = 10)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CampaignError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            if (productId <= 0)
            {
                TempData["CampaignError"] = "Invalid product identifier.";
                return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
            }

            var result = _inventoryService.QueueSlowMoverPromotion(productId);
            TempData[result.Succeeded ? "CampaignMessage" : "CampaignError"] = result.Message;

            return RedirectToAction(nameof(Marketing), new { search, status, audience, page, pageSize });
        }

        // Action of SendQuickMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendQuickMessage(QuickMessageInputViewModel request)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CrmError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectToAction(nameof(Index));
            }

            var subject = request.Subject?.Trim() ?? string.Empty;
            var message = request.Message?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(subject))
            {
                TempData["CrmError"] = "Email subject cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            if (subject.Length > QuickMessageInputViewModel.MaxSubjectLength)
            {
                TempData["CrmError"] = $"Email subject exceeds {QuickMessageInputViewModel.MaxSubjectLength} characters.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                TempData["CrmError"] = "Message body cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            if (message.Length > QuickMessageInputViewModel.MaxMessageLength)
            {
                TempData["CrmError"] = $"Message exceeds {QuickMessageInputViewModel.MaxMessageLength} characters.";
                return RedirectToAction(nameof(Index));
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            var customerQuery = _context.Customers
                .AsNoTracking()
                .Where(customer => customer.CustomerID == request.CustomerId)
                .AsQueryable();

            if (applyOwnerFilter)
            {
                customerQuery = customerQuery.Where(customer =>
                    customer.Orders.Any(order => order.OwnerUserID == ownerUserId));
            }

            var customer = customerQuery
                .Select(customer => new { customer.CustomerID, customer.FullName, customer.Email })
                .FirstOrDefault();

            if (customer is null)
            {
                TempData["CrmError"] = "Customer record was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                TempData["CrmError"] = $"{customer.FullName} has no email address on file.";
                return RedirectToAction(nameof(Index));
            }

            var actorUserId = TryGetCurrentUserId();
            var notif = new EmailNotification
            {
                CustomerID = customer.CustomerID,
                OwnerUserID = applyOwnerFilter ? ownerUserId : null,
                CampaignID = null,
                Subject = subject,
                DeliveryStatus = "Queued",
                ExternalMessageId = string.Empty,
                DateSent = DateTime.UtcNow
            };
            _context.EmailNotifications.Add(notif);
            await _context.SaveChangesAsync();

            var htmlBody = BuildStandardEmailHtml(
                customer.FullName,
                subject,
                message,
                contextLabel: null,
                contextText: null,
                previewLabel: "Customer outreach");

            await _emailOutboxService.QueueEmailAsync(
                customer.Email,
                subject,
                htmlBody,
                applyOwnerFilter ? ownerUserId : null,
                notifId: notif.NotifID);

            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId ?? 1,
                OwnerUserID = applyOwnerFilter ? ownerUserId : null,
                Action = BuildSystemLogAction($"Queued quick email to customer #{customer.CustomerID}"),
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["CrmMessage"] = $"Email send requested for {customer.FullName}. Delivery status will update shortly.";
            return RedirectToAction(nameof(Index));
        }

        // Action of LoyaltyProgram
        public IActionResult LoyaltyProgram()
        {
            return View();
        }

        private static string NormalizeStatusFilter(string? status)
        {
            var normalized = string.IsNullOrWhiteSpace(status) ? "All" : status.Trim();
            var isValid = MarketingCampaignPolicy.GetStatusFilters().Any(option =>
                string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase));

            return isValid ? normalized : "All";
        }

        private static string NormalizeAudienceFilter(string? audience)
        {
            if (string.IsNullOrWhiteSpace(audience))
            {
                return "All";
            }

            var normalized = audience.Trim();
            if (string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase))
            {
                return "All";
            }

            return MarketingCampaignPolicy.IsSupportedAudience(normalized)
                ? MarketingCampaignPolicy.NormalizeAudience(normalized)
                : "All";
        }

        private static bool TryParseBusinessDateTimeLocalInput(string? value, out DateTime localDateTime)
        {
            localDateTime = default;
            var normalized = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return DateTime.TryParseExact(
                normalized,
                new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out localDateTime);
        }

        private MarketingAutomationViewModel BuildMarketingViewModel(
            string? search,
            string? status,
            string? audience,
            int page,
            int pageSize,
            MarketingCampaignCreateInputViewModel? createCampaignForm,
            IReadOnlyCollection<string>? createCampaignErrors,
            bool openCreateCampaignModal)
        {
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;
            var searchTerm = search?.Trim() ?? string.Empty;
            var statusFilter = NormalizeStatusFilter(status);
            var audienceFilter = NormalizeAudienceFilter(audience);
            var normalizedPageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);
            var audienceOptions = MarketingCampaignPolicy.GetSupportedAudiences();
            var audienceInsights = BuildAudienceInsights(applyOwnerFilter, ownerUserId, audienceOptions);
            var audienceEstimateLookup = audienceInsights
                .ToDictionary(insight => insight.Audience, insight => insight.EligibleCustomers, StringComparer.OrdinalIgnoreCase);

            var campaignsQuery = _context.Campaigns
                .AsNoTracking()
                .Where(campaign => !applyOwnerFilter || campaign.OwnerUserID == ownerUserId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var wildcardSearch = $"%{searchTerm}%";
                campaignsQuery = campaignsQuery.Where(campaign =>
                    EF.Functions.Like(campaign.Name, wildcardSearch) ||
                    (campaign.Description != null && EF.Functions.Like(campaign.Description, wildcardSearch)));
            }

            if (!string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                campaignsQuery = campaignsQuery.Where(campaign => campaign.Status == statusFilter);
            }

            if (!string.Equals(audienceFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                campaignsQuery = campaignsQuery.Where(campaign => campaign.TargetAudience == audienceFilter);
            }

            var campaigns = campaignsQuery
                .OrderByDescending(campaign => campaign.StartDate)
                .ToList();

            var campaignIds = campaigns.Select(campaign => campaign.CampaignID).ToList();
            var notificationStats = new Dictionary<int, (int Queued, int Accepted, int Delivered, int Failed)>();
            if (campaignIds.Count > 0)
            {
                var notificationQuery = _context.EmailNotifications
                    .AsNoTracking()
                    .Where(notification => notification.CampaignID.HasValue && campaignIds.Contains(notification.CampaignID.Value))
                    .AsQueryable();

                if (applyOwnerFilter)
                {
                    notificationQuery = notificationQuery.Where(notification => notification.OwnerUserID == ownerUserId);
                }

                // CALC-KPI: Roll up raw delivery statuses into campaign-level counts for marketing KPI cards.
                notificationStats = notificationQuery
                    .GroupBy(notification => notification.CampaignID!.Value)
                    .Select(group => new
                    {
                        CampaignID = group.Key,
                        Queued = group.Count(notification =>
                            notification.DeliveryStatus == "Queued" ||
                            notification.DeliveryStatus == "queued" ||
                            notification.DeliveryStatus == "request" ||
                            notification.DeliveryStatus == "deferred"),
                        Accepted = group.Count(notification =>
                            notification.DeliveryStatus == "Accepted" ||
                            notification.DeliveryStatus == "accepted" ||
                            notification.DeliveryStatus == "Sent" ||
                            notification.DeliveryStatus == "sent"),
                        Delivered = group.Count(notification =>
                            notification.DeliveryStatus == "Delivered" ||
                            notification.DeliveryStatus == "delivered" ||
                            notification.DeliveryStatus == "opened" ||
                            notification.DeliveryStatus == "click" ||
                            notification.DeliveryStatus == "unique_opened" ||
                            notification.DeliveryStatus == "unique_clicked"),
                        Failed = group.Count(notification =>
                            notification.DeliveryStatus == "Failed" ||
                            notification.DeliveryStatus == "hard_bounce" ||
                            notification.DeliveryStatus == "soft_bounce" ||
                            notification.DeliveryStatus == "blocked" ||
                            notification.DeliveryStatus == "invalid" ||
                            notification.DeliveryStatus == "error" ||
                            notification.DeliveryStatus == "spam")
                    })
                    .ToDictionary(
                        group => group.CampaignID,
                        group => (group.Queued, group.Accepted, group.Delivered, group.Failed));
            }

            // CALC-KPI: Compose per-campaign KPI rows including audience counts and delivery performance.
            var campaignRows = campaigns
                .Select(campaign =>
                {
                    notificationStats.TryGetValue(campaign.CampaignID, out var stats);
                    var queued = stats.Queued;
                    var accepted = stats.Accepted;
                    var delivered = stats.Delivered;
                    var failed = stats.Failed;
                    var normalizedAudience = MarketingCampaignPolicy.NormalizeAudience(campaign.TargetAudience);
                    var estimatedAudienceSize = audienceEstimateLookup.TryGetValue(normalizedAudience, out var estimated)
                        ? estimated
                        : 0;

                    return new MarketingCampaignListItemViewModel
                    {
                        CampaignID = campaign.CampaignID,
                        Name = campaign.Name,
                        Description = campaign.Description ?? string.Empty,
                        TargetAudience = normalizedAudience,
                        Status = campaign.Status,
                        ScheduledForUtc = campaign.ScheduledForUtc,
                        StartDate = campaign.StartDate,
                        QueuedCount = queued,
                        AcceptedCount = accepted,
                        DeliveredCount = delivered,
                        FailedCount = failed,
                        AudienceSize = queued + accepted + delivered + failed,
                        EstimatedAudienceSize = estimatedAudienceSize,
                        // CALC-KPI: Delivery rate only uses terminal outcomes (delivered + failed).
                        DeliveryRatePercent = delivered + failed == 0
                            ? 0m
                            : Math.Round((decimal)delivered * 100m / (delivered + failed), 1, MidpointRounding.AwayFromZero),
                        CanExecute = MarketingCampaignPolicy.CanExecuteStatus(campaign.Status),
                        CanCancel = MarketingCampaignPolicy.CanCancelStatus(campaign.Status)
                    };
                })
                .ToList();

            var totalMatched = campaignRows.Count;
            var totalPages = totalMatched <= 0
                ? 0
                : (int)Math.Ceiling(totalMatched / (double)Math.Max(1, normalizedPageSize));
            var currentPage = totalPages == 0
                ? 1
                : Math.Min(Math.Max(1, page), totalPages);
            var pagedCampaignRows = campaignRows
                .Skip((currentPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            var campaignForm = createCampaignForm ?? new MarketingCampaignCreateInputViewModel
            {
                TargetAudience = MarketingCampaignPolicy.GeneralAudience
            };
            campaignForm.Name = campaignForm.Name?.Trim() ?? string.Empty;
            campaignForm.Description = campaignForm.Description?.Trim() ?? string.Empty;
            campaignForm.ScheduledExecutionLocal = campaignForm.ScheduledExecutionLocal?.Trim() ?? string.Empty;
            campaignForm.TargetAudience = MarketingCampaignPolicy.IsSupportedAudience(campaignForm.TargetAudience)
                ? MarketingCampaignPolicy.NormalizeAudience(campaignForm.TargetAudience)
                : MarketingCampaignPolicy.GeneralAudience;

            // CALC-KPI: Aggregate campaign rows into page-level marketing automation summary totals.
            return new MarketingAutomationViewModel
            {
                TotalCampaigns = campaignRows.Count,
                ScheduledCampaigns = campaignRows.Count(campaign =>
                    string.Equals(campaign.Status, MarketingCampaignPolicy.ScheduledStatus, StringComparison.OrdinalIgnoreCase)),
                CompletedCampaigns = campaignRows.Count(campaign =>
                    string.Equals(campaign.Status, MarketingCampaignPolicy.CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(campaign.Status, MarketingCampaignPolicy.CompletedWithErrorsStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(campaign.Status, MarketingCampaignPolicy.NoAudienceStatus, StringComparison.OrdinalIgnoreCase)),
                QueuedEmails = campaignRows.Sum(campaign => campaign.QueuedCount),
                AcceptedEmails = campaignRows.Sum(campaign => campaign.AcceptedCount),
                DeliveredEmails = campaignRows.Sum(campaign => campaign.DeliveredCount),
                FailedEmails = campaignRows.Sum(campaign => campaign.FailedCount),
                SearchTerm = searchTerm,
                SelectedStatus = statusFilter,
                SelectedAudience = audienceFilter,
                AvailableStatuses = MarketingCampaignPolicy.GetStatusFilters().ToList(),
                AvailableAudiences = new[] { "All" }.Concat(audienceOptions).ToList(),
                AudienceInsights = audienceInsights,
                SlowMoverSuggestions = _inventoryService.GetSlowMoverSuggestions(),
                Page = currentPage,
                PageSize = normalizedPageSize,
                TotalMatched = totalMatched,
                Campaigns = pagedCampaignRows,
                CreateCampaignForm = campaignForm,
                CreateCampaignErrors = createCampaignErrors?
                    .Where(error => !string.IsNullOrWhiteSpace(error))
                    .Distinct()
                    .ToList() ?? new List<string>(),
                OpenCreateCampaignModal = openCreateCampaignModal
            };
        }

        private List<MarketingAudienceInsightViewModel> BuildAudienceInsights(
            bool applyOwnerFilter,
            int ownerUserId,
            IReadOnlyList<string> audienceOptions)
        {
            var eligibleCustomers = _context.Customers
                .AsNoTracking()
                .Where(customer =>
                    !applyOwnerFilter ||
                    customer.Orders.Any(order => order.OwnerUserID == ownerUserId))
                .Where(customer => customer.MarketingOptIn && !string.IsNullOrWhiteSpace(customer.Email))
                .Select(customer => new
                {
                    customer.CustomerID,
                    customer.RegistrationDate
                })
                .ToList();

            // CALC-SEGMENT: Precompute per-customer spend and recency for audience insight calculations.
            var customerSpendRows = _context.Orders
                .AsNoTracking()
                .Where(order => !applyOwnerFilter || order.OwnerUserID == ownerUserId)
                .GroupBy(order => order.CustomerID)
                .Select(group => new
                {
                    CustomerID = group.Key,
                    TotalSpent = group.Sum(order => order.TotalAmount),
                    LastOrderDate = (DateTime?)group.Max(order => order.OrderDate)
                })
                .ToList();

            var spendByCustomer = customerSpendRows.ToDictionary(row => row.CustomerID, row => row.TotalSpent);
            var vipCustomerIds = customerSpendRows
                .Where(row => row.TotalSpent >= 100000m)
                .Select(row => row.CustomerID)
                .ToHashSet();

            var lapsedCutoff = DateTime.UtcNow.AddDays(-90);
            var recentActiveCustomerIds = customerSpendRows
                .Where(row => row.LastOrderDate.HasValue && row.LastOrderDate.Value >= lapsedCutoff)
                .Select(row => row.CustomerID)
                .ToHashSet();

            var gpuOwnerIds = _context.OrderDetails
                .AsNoTracking()
                .Where(detail => detail.Order != null &&
                                 (!applyOwnerFilter || detail.Order.OwnerUserID == ownerUserId) &&
                                 detail.Product != null &&
                                 detail.Product.CategoryName == "GPU")
                .Select(detail => detail.Order!.CustomerID)
                .Distinct()
                .ToHashSet();

            var tierByCustomer = eligibleCustomers.ToDictionary(
                customer => customer.CustomerID,
                customer =>
                {
                    var totalSpent = spendByCustomer.TryGetValue(customer.CustomerID, out var spent) ? spent : 0m;
                    return _loyaltyService.ResolveTier(totalSpent).Name;
                });

            var newCustomerCutoff = DateTime.UtcNow.AddDays(-30);

            return audienceOptions
                .Select(audience =>
                {
                    var count = eligibleCustomers.Count;

                    if (string.Equals(audience, MarketingCampaignPolicy.GpuOwnersAudience, StringComparison.OrdinalIgnoreCase))
                    {
                        count = eligibleCustomers.Count(customer => gpuOwnerIds.Contains(customer.CustomerID));
                    }
                    else if (string.Equals(audience, MarketingCampaignPolicy.NewCustomersAudience, StringComparison.OrdinalIgnoreCase))
                    {
                        count = eligibleCustomers.Count(customer => customer.RegistrationDate >= newCustomerCutoff);
                    }
                    else if (string.Equals(audience, MarketingCampaignPolicy.VipAudience, StringComparison.OrdinalIgnoreCase))
                    {
                        count = eligibleCustomers.Count(customer => vipCustomerIds.Contains(customer.CustomerID));
                    }
                    else if (string.Equals(audience, MarketingCampaignPolicy.LapsedCustomersAudience, StringComparison.OrdinalIgnoreCase))
                    {
                        count = eligibleCustomers.Count(customer =>
                            customer.RegistrationDate < lapsedCutoff &&
                            !recentActiveCustomerIds.Contains(customer.CustomerID));
                    }
                    else if (MarketingCampaignPolicy.TryGetTierAudience(audience, out var tierName))
                    {
                        count = eligibleCustomers.Count(customer =>
                            tierByCustomer.TryGetValue(customer.CustomerID, out var tier) &&
                            string.Equals(tier, tierName, StringComparison.OrdinalIgnoreCase));
                    }

                    return new MarketingAudienceInsightViewModel
                    {
                        Audience = audience,
                        Description = MarketingCampaignPolicy.GetAudienceDescription(audience),
                        EligibleCustomers = count
                    };
                })
                .ToList();
        }

        private static string BuildSystemLogAction(string action)
        {
            const int maxLength = 100;
            var normalized = action?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "CRM action";
            }

            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized[..maxLength];
        }

        private int? TryGetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out var userId) ? userId : null;
        }

        private bool IsSuperAdminScopedReadOnly()
        {
            return _tenantContext.IsSuperAdmin &&
                   (!_tenantContext.HasOwnerScope || !_tenantContext.CanEditOwnerWorkspace);
        }

        // Action of Inquiries
        public async Task<IActionResult> Inquiries(string status = "Open")
        {
            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var query = _context.TechnicalInquiries
                .Include(i => i.Customer)
                .AsNoTracking()
                .Where(i => !applyOwnerFilter || i.OwnerUserID == ownerUserId);

            if (status == "Open")
            {
                query = query.Where(i => !i.IsResolved);
            }
            else if (status == "Resolved")
            {
                query = query.Where(i => i.IsResolved);
            }

            var inquiries = await query
                .OrderByDescending(i => i.DateSubmittedUtc)
                .Select(i => new kstech.Models.ViewModels.TechnicalInquiryViewModel
                {
                    InquiryID = i.InquiryID,
                    CustomerID = i.CustomerID,
                    Subject = i.Subject,
                    Message = i.InquiryMessage,
                    CustomerName = i.Customer != null ? i.Customer.FullName : "Unknown",
                    CustomerEmail = i.Customer != null ? i.Customer.Email : "Unknown",
                    DateSubmittedUtc = i.DateSubmittedUtc,
                    IsResolved = i.IsResolved,
                    DateResolvedUtc = i.DateResolvedUtc,
                    ResolutionNotes = i.ResolutionNotes
                })
                .ToListAsync();

            var inquiryCustomerIds = inquiries
                .Select(i => i.CustomerID)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var emailActivityByCustomerId = new Dictionary<int, List<kstech.Models.ViewModels.InquiryEmailActivityViewModel>>();
            if (inquiryCustomerIds.Count > 0)
            {
                var emailRows = await _context.EmailNotifications
                    .AsNoTracking()
                    .Where(notification => inquiryCustomerIds.Contains(notification.CustomerID))
                    .Where(notification => notification.CampaignID == null)
                    .Where(notification => !applyOwnerFilter || notification.OwnerUserID == ownerUserId)
                    .OrderByDescending(notification => notification.DateSent)
                    .Select(notification => new
                    {
                        notification.CustomerID,
                        notification.Subject,
                        notification.DeliveryStatus,
                        notification.DateSent
                    })
                    .ToListAsync();

                emailActivityByCustomerId = emailRows
                    .GroupBy(row => row.CustomerID)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(row => row.DateSent)
                            .Take(8)
                            .Select(row => new kstech.Models.ViewModels.InquiryEmailActivityViewModel
                            {
                                Subject = row.Subject,
                                DeliveryStatus = row.DeliveryStatus,
                                DateSentUtc = row.DateSent,
                                Source = row.Subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase)
                                    ? "Reply Email"
                                    : "Direct Email"
                            })
                            .ToList());
            }

            var model = new kstech.Models.ViewModels.TechnicalInquiryListViewModel
            {
                Inquiries = inquiries,
                FilterStatus = status,
                EmailActivityByCustomerId = emailActivityByCustomerId
            };

            return View(model);
        }

        // Action of Inquiry
        [HttpGet]
        public async Task<IActionResult> Inquiry(int id, string status = "Open")
        {
            if (id <= 0)
            {
                TempData["CrmError"] = "Invalid inquiry identifier.";
                return RedirectToAction(nameof(Inquiries), new { status });
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var inquiryEntity = await _context.TechnicalInquiries
                .Include(i => i.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InquiryID == id && (!applyOwnerFilter || i.OwnerUserID == ownerUserId));

            if (inquiryEntity == null)
            {
                TempData["CrmError"] = "Inquiry not found.";
                return RedirectToAction(nameof(Inquiries), new { status });
            }

            var inquiry = new kstech.Models.ViewModels.TechnicalInquiryViewModel
            {
                InquiryID = inquiryEntity.InquiryID,
                CustomerID = inquiryEntity.CustomerID,
                Subject = inquiryEntity.Subject,
                Message = inquiryEntity.InquiryMessage,
                CustomerName = inquiryEntity.Customer?.FullName ?? "Unknown",
                CustomerEmail = inquiryEntity.Customer?.Email ?? "Unknown",
                DateSubmittedUtc = inquiryEntity.DateSubmittedUtc,
                IsResolved = inquiryEntity.IsResolved,
                DateResolvedUtc = inquiryEntity.DateResolvedUtc,
                ResolutionNotes = inquiryEntity.ResolutionNotes
            };

            var emailActivity = await _context.EmailNotifications
                .AsNoTracking()
                .Where(notification => notification.CustomerID == inquiry.CustomerID)
                .Where(notification => notification.CampaignID == null)
                .Where(notification => !applyOwnerFilter || notification.OwnerUserID == ownerUserId)
                .OrderByDescending(notification => notification.DateSent)
                .Take(20)
                .Select(notification => new kstech.Models.ViewModels.InquiryEmailActivityViewModel
                {
                    Subject = notification.Subject,
                    DeliveryStatus = notification.DeliveryStatus,
                    DateSentUtc = notification.DateSent,
                    Source = notification.Subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase)
                        ? "Reply Email"
                        : "Direct Email"
                })
                .ToListAsync();

            var model = new kstech.Models.ViewModels.TechnicalInquiryDetailViewModel
            {
                Inquiry = inquiry,
                EmailActivity = emailActivity,
                ListStatus = string.IsNullOrWhiteSpace(status) ? "Open" : status
            };

            return View(model);
        }

        // Action of ReplyInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyInquiry(int inquiryId, string? replyMessage, bool returnToDetail = false, string? status = null)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CrmError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectAfterInquiryPost(inquiryId, returnToDetail, status);
            }

            var normalizedReply = replyMessage?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedReply))
            {
                TempData["CrmError"] = "Reply message cannot be empty.";
                return RedirectAfterInquiryPost(inquiryId, returnToDetail, status);
            }

            if (normalizedReply.Length > 1000)
            {
                TempData["CrmError"] = "Reply message cannot exceed 1000 characters.";
                return RedirectAfterInquiryPost(inquiryId, returnToDetail, status);
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var inquiry = await _context.TechnicalInquiries
                .Include(i => i.Customer)
                .FirstOrDefaultAsync(i => i.InquiryID == inquiryId && (!applyOwnerFilter || i.OwnerUserID == ownerUserId));

            if (inquiry == null)
            {
                TempData["CrmError"] = "Inquiry not found.";
                return RedirectAfterInquiryPost(inquiryId, returnToDetail, status);
            }

            if (inquiry.Customer == null || string.IsNullOrWhiteSpace(inquiry.Customer.Email))
            {
                TempData["CrmError"] = "Customer email is not available for this inquiry.";
                return RedirectAfterInquiryPost(inquiryId, returnToDetail, status);
            }

            var subject = BuildReplyEmailSubject(inquiry.Subject);
            var customerName = string.IsNullOrWhiteSpace(inquiry.Customer.FullName) ? "Customer" : inquiry.Customer.FullName;
            var htmlBody = BuildStandardEmailHtml(
                customerName,
                subject,
                normalizedReply,
                contextLabel: "Original inquiry",
                contextText: inquiry.InquiryMessage,
                previewLabel: "Support reply");

            var notif = new EmailNotification
            {
                CustomerID = inquiry.CustomerID,
                OwnerUserID = applyOwnerFilter ? ownerUserId : null,
                CampaignID = null,
                Subject = subject,
                DeliveryStatus = "Queued",
                ExternalMessageId = string.Empty,
                DateSent = DateTime.UtcNow
            };
            _context.EmailNotifications.Add(notif);
            await _context.SaveChangesAsync();

            await _emailOutboxService.QueueEmailAsync(
                inquiry.Customer.Email,
                subject,
                htmlBody,
                applyOwnerFilter ? ownerUserId : null,
                notifId: notif.NotifID);

            var actorUserId = TryGetCurrentUserId();
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = actorUserId ?? 1,
                OwnerUserID = applyOwnerFilter ? ownerUserId : null,
                Action = BuildSystemLogAction($"Queued inquiry reply for #{inquiry.InquiryID}"),
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["CrmMessage"] = $"Reply send requested for inquiry #{inquiry.InquiryID}. Inquiry remains open until you mark it resolved.";
            return RedirectAfterInquiryPost(inquiry.InquiryID, returnToDetail, status);
        }

        // Action of ResolveInquiry
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveInquiry(int InquiryID, string? ResolutionNotes, bool returnToDetail = false, string? status = null)
        {
            if (IsSuperAdminScopedReadOnly())
            {
                TempData["CrmError"] = "Select an owner workspace with edit permission before making changes.";
                return RedirectAfterInquiryPost(InquiryID, returnToDetail, status);
            }

            var applyOwnerFilter = _tenantContext.HasOwnerScope;
            var ownerUserId = _tenantContext.OwnerUserId ?? 0;

            var inquiry = await _context.TechnicalInquiries
                .Include(i => i.Customer)
                .FirstOrDefaultAsync(i => i.InquiryID == InquiryID && (!applyOwnerFilter || i.OwnerUserID == ownerUserId));

            if (inquiry == null)
            {
                TempData["CrmError"] = "Inquiry not found.";
                return RedirectAfterInquiryPost(InquiryID, returnToDetail, status);
            }

            if (inquiry.IsResolved)
            {
                TempData["CrmError"] = "Inquiry is already resolved.";
                return RedirectAfterInquiryPost(InquiryID, returnToDetail, status);
            }

            inquiry.IsResolved = true;
            inquiry.DateResolvedUtc = DateTime.UtcNow;
            inquiry.ResolutionNotes = string.IsNullOrWhiteSpace(ResolutionNotes) ? null : ResolutionNotes.Trim();
            await _context.SaveChangesAsync();

            TempData["CrmMessage"] = $"Inquiry #{inquiry.InquiryID} marked as resolved.";
            return RedirectAfterInquiryPost(inquiry.InquiryID, returnToDetail, status);
        }

        private IActionResult RedirectAfterInquiryPost(int inquiryId, bool returnToDetail, string? status)
        {
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "Open" : status;
            if (returnToDetail && inquiryId > 0)
            {
                return RedirectToAction(nameof(Inquiry), new { id = inquiryId, status = normalizedStatus });
            }

            return RedirectToAction(nameof(Inquiries), new { status = normalizedStatus });
        }

        private static string BuildReplyEmailSubject(string inquirySubject)
        {
            const string prefix = "RE: ";
            var normalizedSubject = inquirySubject?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedSubject))
            {
                normalizedSubject = "Customer Inquiry";
            }

            var maxSubjectLength = 50;
            var availableLength = Math.Max(0, maxSubjectLength - prefix.Length);
            if (normalizedSubject.Length > availableLength)
            {
                normalizedSubject = normalizedSubject[..availableLength];
            }

            return $"{prefix}{normalizedSubject}";
        }

        private static string BuildStandardEmailHtml(
            string? recipientName,
            string subject,
            string bodyText,
            string? contextLabel,
            string? contextText,
            string previewLabel)
        {
            var safeRecipientName = string.IsNullOrWhiteSpace(recipientName) ? "Customer" : recipientName.Trim();
            var safeSubject = System.Net.WebUtility.HtmlEncode(subject?.Trim() ?? "KSTech Update");
            var safePreviewLabel = System.Net.WebUtility.HtmlEncode(previewLabel?.Trim() ?? "KSTech message");
            var formattedBody = FormatEmailMultilineText(bodyText);

            var contextBlock = string.Empty;
            if (!string.IsNullOrWhiteSpace(contextLabel) && !string.IsNullOrWhiteSpace(contextText))
            {
                var safeContextLabel = System.Net.WebUtility.HtmlEncode(contextLabel.Trim());
                var formattedContext = FormatEmailMultilineText(contextText);
                contextBlock = $@"
                    <div style=""margin-top:18px;border:1px solid #e5e7eb;border-radius:10px;background:#f8fafc;padding:14px;"">
                        <div style=""font-size:11px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;color:#64748b;margin-bottom:8px;"">{safeContextLabel}</div>
                        <div style=""font-size:14px;line-height:1.6;color:#334155;"">{formattedContext}</div>
                    </div>";
            }

            var safeNameHtml = System.Net.WebUtility.HtmlEncode(safeRecipientName);

            return $@"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>{safeSubject}</title>
</head>
<body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;"">
  <div style=""padding:24px 12px;"">
    <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""max-width:640px;margin:0 auto;"">
      <tr>
        <td style=""padding:0 0 12px 4px;font-size:12px;color:#64748b;"">{safePreviewLabel}</td>
      </tr>
      <tr>
        <td style=""background:#ffffff;border:1px solid #e5e7eb;border-radius:14px;overflow:hidden;"">
          <div style=""background:linear-gradient(135deg,#0b3b3a,#236460);padding:18px 22px;color:#ffffff;"">
            <div style=""font-size:22px;font-weight:700;letter-spacing:.2px;"">KSTech</div>
            <div style=""margin-top:6px;font-size:13px;opacity:.92;"">{safeSubject}</div>
          </div>
          <div style=""padding:22px;"">
            <div style=""font-size:15px;line-height:1.55;color:#0f172a;"">
              Hello {safeNameHtml},
            </div>
            <div style=""margin-top:14px;font-size:14px;line-height:1.7;color:#334155;"">
              {formattedBody}
            </div>
            {contextBlock}
            <div style=""margin-top:20px;font-size:14px;line-height:1.6;color:#334155;"">
              Best regards,<br />
              <strong style=""color:#0f172a;"">KSTech Support</strong>
            </div>
          </div>
          <div style=""border-top:1px solid #e5e7eb;background:#f8fafc;padding:14px 22px;font-size:12px;color:#64748b;line-height:1.5;"">
            This is an email from KSTech CRM. Replies will go to the sender mailbox configured for your support workflow.
          </div>
        </td>
      </tr>
    </table>
  </div>
</body>
</html>";
        }

        private static string FormatEmailMultilineText(string? value)
        {
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var encoded = System.Net.WebUtility.HtmlEncode(normalized);
            return encoded.Replace("\n", "<br />");
        }
    }
}
