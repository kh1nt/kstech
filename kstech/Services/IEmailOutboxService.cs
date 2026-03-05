namespace kstech.Services
{
    public interface IEmailOutboxService
    {
        Task<int> QueueEmailAsync(string recipientEmail, string subject, string htmlBody, int? ownerUserId = null, int? notifId = null);
    }
}
