using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using kstech.Configuration;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    public record BrevoSendResult(bool Success, string MessageId, string ErrorMessage);

    public interface IBrevoEmailService
    {
        Task<BrevoSendResult> SendEmailAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlContent,
            CancellationToken cancellationToken = default);
    }

    public class BrevoEmailService : IBrevoEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly BrevoOptions _options;
        private readonly ILogger<BrevoEmailService> _logger;

        public BrevoEmailService(
            HttpClient httpClient,
            IOptions<BrevoOptions> options,
            ILogger<BrevoEmailService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<BrevoSendResult> SendEmailAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlContent,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                return new BrevoSendResult(false, string.Empty, "Brevo is disabled.");
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
                string.IsNullOrWhiteSpace(_options.SenderEmail))
            {
                return new BrevoSendResult(false, string.Empty, "Brevo API key or sender email is missing.");
            }

            var requestBody = new
            {
                sender = new { email = _options.SenderEmail, name = _options.SenderName },
                to = new[] { new { email = recipientEmail, name = recipientName } },
                subject,
                htmlContent
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/smtp/email");
            request.Headers.Add("api-key", _options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Brevo send failed with status {StatusCode}: {Body}", response.StatusCode, raw);
                    return new BrevoSendResult(false, string.Empty, raw);
                }

                using var doc = JsonDocument.Parse(raw);
                var messageId = doc.RootElement.TryGetProperty("messageId", out var messageIdElement)
                    ? messageIdElement.GetString() ?? string.Empty
                    : string.Empty;

                return new BrevoSendResult(true, messageId, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brevo send threw an exception.");
                return new BrevoSendResult(false, string.Empty, ex.Message);
            }
        }
    }
}
