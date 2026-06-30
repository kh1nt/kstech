namespace kstech.Services
{
    public static class PasswordPolicy
    {
        public const int MinLength = 10;
        public const string RequirementsText =
            "Password must be at least 10 characters and include an uppercase letter, a lowercase letter, a number, and a special character (e.g. !@#$%^&*).";

        public static bool TryValidate(string? password, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                errorMessage = "Password is required.";
                return false;
            }

            if (password.Length < MinLength)
            {
                errorMessage = RequirementsText;
                return false;
            }

            var hasUpper   = password.Any(char.IsUpper);
            var hasLower   = password.Any(char.IsLower);
            var hasDigit   = password.Any(char.IsDigit);
            var hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));

            if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
            {
                errorMessage = RequirementsText;
                return false;
            }

            return true;
        }
    }
}
