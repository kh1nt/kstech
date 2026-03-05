using kstech.Data;
using kstech.Models.Entities;

namespace kstech.Services
{
    public class EmailOutboxService : IEmailOutboxService
    {
        private readonly ApplicationDbContext _dbContext;

        public EmailOutboxService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<int> QueueEmailAsync(string recipientEmail, string subject, string htmlBody, int? ownerUserId = null, int? notifId = null)
        {
            var outboxItem = new EmailOutbox
            {
                RecipientEmail = recipientEmail,
                Subject = subject,
                HtmlBody = htmlBody,
                OwnerUserID = ownerUserId,
                NotifID = notifId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.EmailOutbox.Add(outboxItem);
            await _dbContext.SaveChangesAsync();
            return outboxItem.OutboxID;
        }
    }
}

