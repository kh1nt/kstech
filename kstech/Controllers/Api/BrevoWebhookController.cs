using System.Text.Json;
using kstech.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace kstech.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous] // Webhooks typically come from outside without normal user auth
    public class BrevoWebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BrevoWebhookController> _logger;

        public BrevoWebhookController(ApplicationDbContext context, ILogger<BrevoWebhookController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Action of Post
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] JsonElement payload)
        {
            try
            {
                // Brevo payload format includes 'event' and 'message-id'
                if (payload.TryGetProperty("event", out var eventProperty) &&
                    payload.TryGetProperty("message-id", out var messageIdProperty))
                {
                    string rawEvent = eventProperty.GetString() ?? "Unknown";
                    string status = NormalizeBrevoStatus(rawEvent);
                    string messageId = messageIdProperty.GetString() ?? string.Empty;

                    if (!string.IsNullOrEmpty(messageId))
                    {
                        var notification = await _context.EmailNotifications
                            .FirstOrDefaultAsync(n => n.ExternalMessageId == messageId);

                        if (notification != null)
                        {
                            notification.DeliveryStatus = status;
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Updated email {MessageId} status to {Status} (raw event: {RawEvent})", messageId, status, rawEvent);
                        }
                        else
                        {
                            _logger.LogWarning("Webhook received for unknown message ID: {MessageId}", messageId);
                        }
                    }
                }

                return Ok(); // Always return 200 OK so Brevo knows we got it
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Brevo webhook");
                return StatusCode(500);
            }
        }

        private static string NormalizeBrevoStatus(string? brevoEvent)
        {
            var normalized = (brevoEvent ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Unknown";
            }

            if (normalized is "delivered" or "opened" or "click" or "unique_opened" or "unique_clicked")
            {
                return "Delivered";
            }

            if (normalized is "sent")
            {
                return "Accepted";
            }

            if (normalized is "request" or "deferred" or "queued")
            {
                return "Queued";
            }

            if (normalized is "hard_bounce" or "soft_bounce" or "blocked" or "invalid" or "error" or "spam")
            {
                return "Failed";
            }

            if (normalized is "unsubscribed")
            {
                return "Unsubscribed";
            }

            return brevoEvent ?? "Unknown";
        }
    }
}
