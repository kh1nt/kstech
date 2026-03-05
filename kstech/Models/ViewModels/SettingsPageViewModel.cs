namespace kstech.Models.ViewModels
{
    public class SettingsPageViewModel
    {
        public UserProfileViewModel Profile { get; set; } = new();
        public ChangePasswordViewModel Security { get; set; } = new();
        public string ActiveSection { get; set; } = "profile";
    }
}
