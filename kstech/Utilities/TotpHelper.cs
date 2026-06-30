using System;
using System.Security.Cryptography;
using System.Text;

namespace kstech.Utilities
{
    public static class TotpHelper
    {
        private static readonly string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Generates a new 160-bit cryptographically secure random secret key, encoded in Base32.
        /// </summary>
        public static string GenerateSecret()
        {
            var bytes = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return EncodeBase32(bytes);
        }

        /// <summary>
        /// Generates the standard otpauth URL that can be encoded in a QR code.
        /// </summary>
        public static string GenerateOtpAuthUrl(string email, string issuer, string secretBase32)
        {
            var normalizedEmail = Uri.EscapeDataString(email);
            var normalizedIssuer = Uri.EscapeDataString(issuer);
            return $"otpauth://totp/{normalizedIssuer}:{normalizedEmail}?secret={secretBase32}&issuer={normalizedIssuer}&algorithm=SHA1&digits=6&period=30";
        }

        /// <summary>
        /// Verifies a 6-digit TOTP code against a Base32-encoded secret, allowing for clock drift.
        /// </summary>
        public static bool VerifyCode(string secretBase32, string code, int allowedDriftSteps = 1)
        {
            if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var cleanSecret = secretBase32.Replace(" ", "").Replace("-", "").ToUpperInvariant();
            var cleanCode = code.Replace(" ", "").Replace("-", "");

            if (cleanCode.Length != 6 || !int.TryParse(cleanCode, out _))
            {
                return false;
            }

            byte[] secretBytes;
            try
            {
                secretBytes = DecodeBase32(cleanSecret);
            }
            catch
            {
                return false; // Invalid Base32 encoding
            }

            var currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var currentStep = currentUnixTime / 30;

            for (int i = -allowedDriftSteps; i <= allowedDriftSteps; i++)
            {
                var step = currentStep + i;
                var calculatedCode = CalculateTotp(secretBytes, step);
                if (string.Equals(calculatedCode, cleanCode, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string CalculateTotp(byte[] secret, long step)
        {
            // Convert step to 8-byte big-endian representation
            var stepBytes = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(stepBytes);
            }

            using (var hmac = new HMACSHA1(secret))
            {
                var hash = hmac.ComputeHash(stepBytes);
                
                // Dynamic Truncation
                int offset = hash[hash.Length - 1] & 0x0F;
                int binaryCode = ((hash[offset] & 0x7F) << 24)
                               | ((hash[offset + 1] & 0xFF) << 16)
                               | ((hash[offset + 2] & 0xFF) << 8)
                               | (hash[offset + 3] & 0xFF);

                int otp = binaryCode % 1000000;
                return otp.ToString("D6");
            }
        }

        private static string EncodeBase32(byte[] data)
        {
            var result = new StringBuilder((data.Length + 7) * 8 / 5);
            int binVal = 0;
            int binLen = 0;
            foreach (var b in data)
            {
                binVal = (binVal << 8) | b;
                binLen += 8;
                while (binLen >= 5)
                {
                    result.Append(Base32Alphabet[(binVal >> (binLen - 5)) & 0x1F]);
                    binLen -= 5;
                }
            }
            if (binLen > 0)
            {
                result.Append(Base32Alphabet[(binVal << (5 - binLen)) & 0x1F]);
            }
            return result.ToString();
        }

        private static byte[] DecodeBase32(string base32)
        {
            var result = new byte[base32.Length * 5 / 8];
            int byteIndex = 0;
            int binVal = 0;
            int binLen = 0;
            foreach (char c in base32)
            {
                int val = Base32Alphabet.IndexOf(c);
                if (val < 0)
                {
                    throw new ArgumentException("Invalid character in Base32 string.");
                }
                binVal = (binVal << 5) | val;
                binLen += 5;
                if (binLen >= 8)
                {
                    result[byteIndex] = (byte)((binVal >> (binLen - 8)) & 0xFF);
                    byteIndex++;
                    binLen -= 8;
                }
            }
            return result;
        }
    }
}
