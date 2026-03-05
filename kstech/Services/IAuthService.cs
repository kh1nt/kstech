using System.Security.Claims;
using kstech.Models.Entities;

namespace kstech.Services
{
    public interface IAuthService
    {
        Task<User?> AuthenticateAsync(string email, string password);
        Task<User> RegisterAsync(string email, string password, string fullName, string role, string userType, int? ownerUserId = null);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
        Task SignInAsync(User user, bool isPersistent, string scheme);
        Task SignOutAsync(string scheme);
    }
}
