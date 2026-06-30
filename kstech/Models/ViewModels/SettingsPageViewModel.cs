namespace kstech.Models.ViewModels
{
    public class SettingsPageViewModel
    {
        public UserProfileViewModel Profile { get; set; } = new();
        public ChangePasswordViewModel Security { get; set; } = new();
        public string ActiveSection { get; set; } = "profile";
        public bool TwoFactorEnabled { get; set; }
        public string? TwoFactorSecret { get; set; }
        public string? TwoFactorQrUrl { get; set; }
    }
}
