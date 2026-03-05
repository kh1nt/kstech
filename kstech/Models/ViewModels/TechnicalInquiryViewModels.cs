using System;
using System.Collections.Generic;

namespace kstech.Models.ViewModels
{
    public class TechnicalInquiryViewModel
    {
        public int InquiryID { get; set; }
        public int CustomerID { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public DateTime DateSubmittedUtc { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? DateResolvedUtc { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    public class InquiryEmailActivityViewModel
    {
        public string Subject { get; set; } = string.Empty;
        public string DeliveryStatus { get; set; } = string.Empty;
        public string Source { get; set; } = "Direct Email";
        public DateTime DateSentUtc { get; set; }
    }

    public class TechnicalInquiryListViewModel
    {
        public List<TechnicalInquiryViewModel> Inquiries { get; set; } = new();
        public string FilterStatus { get; set; } = "Open";
        public Dictionary<int, List<InquiryEmailActivityViewModel>> EmailActivityByCustomerId { get; set; } = new();
    }

    public class TechnicalInquiryDetailViewModel
    {
        public TechnicalInquiryViewModel Inquiry { get; set; } = new();
        public List<InquiryEmailActivityViewModel> EmailActivity { get; set; } = new();
        public string ListStatus { get; set; } = "Open";
    }
}
