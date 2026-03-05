using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using kstech.Configuration;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    public interface IReportCloudArchiveService
    {
        ReportCloudArchiveResult TryUploadReport(
            string reportType,
            string fileName,
            byte[] reportBytes,
            int? ownerUserId,
            DateTime periodStartLocal,
            DateTime periodEndLocal);
    }

    public record ReportCloudArchiveResult(
        bool Uploaded,
        string? SecureUrl,
        string? PublicId,
        string Message);

    public class ReportCloudArchiveService : IReportCloudArchiveService
    {
        private readonly CloudinaryOptions _options;
        private readonly ILogger<ReportCloudArchiveService> _logger;

        public ReportCloudArchiveService(
            IOptions<CloudinaryOptions> options,
            ILogger<ReportCloudArchiveService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public ReportCloudArchiveResult TryUploadReport(
            string reportType,
            string fileName,
            byte[] reportBytes,
            int? ownerUserId,
            DateTime periodStartLocal,
            DateTime periodEndLocal)
        {
            if (reportBytes == null || reportBytes.Length == 0)
            {
                return new ReportCloudArchiveResult(false, null, null, "Empty report payload.");
            }

            if (!_options.Enabled)
            {
                return new ReportCloudArchiveResult(false, null, null, "Cloudinary disabled in configuration.");
            }

            if (string.IsNullOrWhiteSpace(_options.CloudName) ||
                string.IsNullOrWhiteSpace(_options.ApiKey) ||
                string.IsNullOrWhiteSpace(_options.ApiSecret))
            {
                return new ReportCloudArchiveResult(false, null, null, "Cloudinary credentials are missing.");
            }

            var normalizedReportType = Slugify(reportType, "report");
            var folder = BuildFolder(normalizedReportType, ownerUserId);
            var publicId = BuildPublicId(normalizedReportType, periodStartLocal, periodEndLocal);

            try
            {
                var account = new Account(_options.CloudName, _options.ApiKey, _options.ApiSecret);
                var cloudinary = new Cloudinary(account);

                using var stream = new MemoryStream(reportBytes);
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = folder,
                    PublicId = publicId,
                    UseFilename = false,
                    UniqueFilename = false,
                    Overwrite = false
                };

                var uploadResult = cloudinary.Upload(uploadParams);
                if (uploadResult.Error != null)
                {
                    _logger.LogWarning(
                        "Cloudinary report upload failed for type {ReportType}. Error: {Error}",
                        reportType,
                        uploadResult.Error.Message);

                    return new ReportCloudArchiveResult(
                        false,
                        null,
                        null,
                        $"Cloudinary upload failed: {uploadResult.Error.Message}");
                }

                var secureUrl = uploadResult.SecureUrl?.ToString();
                var uploadedPublicId = uploadResult.PublicId;

                _logger.LogInformation(
                    "Cloudinary report uploaded. Type: {ReportType}, OwnerUserId: {OwnerUserId}, PublicId: {PublicId}",
                    reportType,
                    ownerUserId,
                    uploadedPublicId);

                return new ReportCloudArchiveResult(
                    true,
                    secureUrl,
                    uploadedPublicId,
                    "Cloudinary upload completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while uploading report {ReportType} to Cloudinary.", reportType);
                return new ReportCloudArchiveResult(false, null, null, $"Cloudinary upload exception: {ex.Message}");
            }
        }

        private string BuildFolder(string normalizedReportType, int? ownerUserId)
        {
            var baseFolder = NormalizeFolder(_options.ReportFolder);
            var ownerSegment = ownerUserId.HasValue ? $"owner-{ownerUserId.Value}" : "owner-unscoped";
            return $"{baseFolder}/{ownerSegment}/{normalizedReportType}";
        }

        private static string BuildPublicId(
            string normalizedReportType,
            DateTime periodStartLocal,
            DateTime periodEndLocal)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var shortGuid = Guid.NewGuid().ToString("N")[..8];
            var dateRange = $"{periodStartLocal:yyyyMMdd}-{periodEndLocal:yyyyMMdd}";
            return $"{normalizedReportType}-{dateRange}-{timestamp}-{shortGuid}";
        }

        private static string NormalizeFolder(string? folder)
        {
            var segments = (folder ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => Slugify(segment, string.Empty))
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToList();

            if (segments.Count == 0)
            {
                return "kstech/reports";
            }

            return string.Join("/", segments);
        }

        private static string Slugify(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
            normalized = Regex.Replace(normalized, @"-+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }
    }
}
