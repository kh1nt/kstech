using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace kstech.Services
{
    public static class PasswordResetTokenHelper
    {
        public static string GenerateRawToken(int byteLength = 32)
        {
            var tokenBytes = RandomNumberGenerator.GetBytes(byteLength);
            return WebEncoders.Base64UrlEncode(tokenBytes);
        }

        public static string HashToken(string? token)
        {
            var normalized = (token ?? string.Empty).Trim();
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hashBytes);
        }
    }
}
