using kstech.Models.ViewModels;

namespace kstech.Models
{
    public class MarketingAutomationViewModel
    {
        public int TotalCampaigns { get; set; }
        public int ScheduledCampaigns { get; set; }
        public int CompletedCampaigns { get; set; }
        public int QueuedEmails { get; set; }
        public int AcceptedEmails { get; set; }
        public int DeliveredEmails { get; set; }
        public int FailedEmails { get; set; }
        public string SearchTerm { get; set; } = string.Empty;
        public string SelectedStatus { get; set; } = "All";
        public string SelectedAudience { get; set; } = "All";
        public List<string> AvailableStatuses { get; set; } = new List<string>();
        public List<string> AvailableAudiences { get; set; } = new List<string>();
        public List<MarketingAudienceInsightViewModel> AudienceInsights { get; set; } = new List<MarketingAudienceInsightViewModel>();
        public List<SlowMoverSuggestionViewModel> SlowMoverSuggestions { get; set; } = new List<SlowMoverSuggestionViewModel>();
        public MarketingCampaignCreateInputViewModel CreateCampaignForm { get; set; } = new MarketingCampaignCreateInputViewModel();
        public List<string> CreateCampaignErrors { get; set; } = new List<string>();
        public bool OpenCreateCampaignModal { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalMatched { get; set; }
        public int TotalPages => TotalMatched <= 0
            ? 0
            : (int)Math.Ceiling(TotalMatched / (double)Math.Max(1, PageSize));
        public int StartItem => TotalMatched == 0 ? 0 : ((Page - 1) * PageSize) + 1;
        public int EndItem => TotalMatched == 0 ? 0 : Math.Min(Page * PageSize, TotalMatched);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
        public List<MarketingCampaignListItemViewModel> Campaigns { get; set; } = new List<MarketingCampaignListItemViewModel>();
    }

    public class MarketingCampaignListItemViewModel
    {
        public int CampaignID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetAudience { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public DateTime? ScheduledForUtc { get; set; }
        public DateTime StartDate { get; set; }
        public int AudienceSize { get; set; }
        public int EstimatedAudienceSize { get; set; }
        public int QueuedCount { get; set; }
        public int AcceptedCount { get; set; }
        public int DeliveredCount { get; set; }
        public int FailedCount { get; set; }
        public decimal DeliveryRatePercent { get; set; }
        public bool CanExecute { get; set; }
        public bool CanCancel { get; set; }
    }

    public class MarketingAudienceInsightViewModel
    {
        public string Audience { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int EligibleCustomers { get; set; }
    }

    public class MarketingCampaignCreateInputViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetAudience { get; set; } = string.Empty;
        public string ScheduledExecutionLocal { get; set; } = string.Empty;
    }
}
