namespace kstech.Services
{
    public static class PasswordPolicy
    {
        public const int MinLength = 8;
        public const string RequirementsText = "Password must be at least 8 characters and include an uppercase letter, a lowercase letter, and a number.";

        public static bool TryValidate(string? password, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "Password is required.";
                return false;
            }

            if (password.Length < MinLength)
            {
                errorMessage = RequirementsText;
                return false;
            }

            var hasUpper = password.Any(char.IsUpper);
            var hasLower = password.Any(char.IsLower);
            var hasDigit = password.Any(char.IsDigit);

            if (!hasUpper || !hasLower || !hasDigit)
            {
                errorMessage = RequirementsText;
                return false;
            }

            return true;
        }
    }
}
