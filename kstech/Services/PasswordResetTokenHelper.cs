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

        /// <summary>
        /// SHA-256 hash a short OTP code (e.g. 6-digit email verification code) before
        /// storing it in the database. The raw code is sent to the user; only the hash is persisted.
        /// </summary>
        public static string HashOtp(string rawCode)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode));
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Constant-time comparison for OTP hashes to prevent timing-based enumeration.
        /// Hash the user-supplied input before calling this method.
        /// </summary>
        public static bool FixedTimeEqualsOtp(string storedHash, string candidateHash)
        {
            if (storedHash.Length != candidateHash.Length)
            {
                return false;
            }

            var storedBytes    = Encoding.UTF8.GetBytes(storedHash);
            var candidateBytes = Encoding.UTF8.GetBytes(candidateHash);
            return CryptographicOperations.FixedTimeEquals(storedBytes, candidateBytes);
        }
    }
}
