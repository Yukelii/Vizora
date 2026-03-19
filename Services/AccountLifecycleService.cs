using Microsoft.AspNetCore.Identity;
using Vizora.Models;

namespace Vizora.Services
{
    public interface IAccountLifecycleService
    {
        Task<SignInResult> PasswordSignInAsync(string userName, string password, bool rememberMe);

        Task<RegistrationResult> RegisterAsync(string userName, string email, string password);

        Task SignOutAsync();

        Task<ApplicationUser?> FindByEmailAsync(string email);

        Task<ApplicationUser?> FindByUserNameAsync(string userName);

        Task<ApplicationUser?> FindByIdAsync(string userId);

        Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user);

        Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user);

        Task<IdentityResult> ConfirmEmailAsync(ApplicationUser user, string token);

        Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword);

        Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword);

        Task RefreshSignInAsync(ApplicationUser user);

        Task<bool> IsEmailConfirmedAsync(ApplicationUser user);

        Task<bool> SendEmailConfirmationAsync(ApplicationUser user, string confirmationLink);

        Task<bool> SendPasswordResetEmailAsync(ApplicationUser user, string resetLink);
    }

    public sealed class RegistrationResult
    {
        public RegistrationResult(IdentityResult identityResult, ApplicationUser? user)
        {
            IdentityResult = identityResult;
            User = user;
        }

        public IdentityResult IdentityResult { get; }

        public ApplicationUser? User { get; }
    }

    public class AccountLifecycleService : IAccountLifecycleService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IAccountEmailService _accountEmailService;

        public AccountLifecycleService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IAccountEmailService accountEmailService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _accountEmailService = accountEmailService;
        }

        public Task<SignInResult> PasswordSignInAsync(string userName, string password, bool rememberMe)
        {
            // Lockout-on-failure is enabled to reduce brute-force risk.
            return _signInManager.PasswordSignInAsync(userName, password, rememberMe, lockoutOnFailure: true);
        }

        public async Task<RegistrationResult> RegisterAsync(string userName, string email, string password)
        {
            // Identity handles password policy and persistence; we only supply initial fields.
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            var identityResult = await _userManager.CreateAsync(user, password);
            return new RegistrationResult(identityResult, identityResult.Succeeded ? user : null);
        }

        public Task SignOutAsync()
        {
            return _signInManager.SignOutAsync();
        }

        public Task<ApplicationUser?> FindByEmailAsync(string email)
        {
            return _userManager.FindByEmailAsync(email);
        }

        public Task<ApplicationUser?> FindByUserNameAsync(string userName)
        {
            return _userManager.FindByNameAsync(userName);
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId)
        {
            return _userManager.FindByIdAsync(userId);
        }

        public Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user)
        {
            return _userManager.GenerateEmailConfirmationTokenAsync(user);
        }

        public Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user)
        {
            return _userManager.GeneratePasswordResetTokenAsync(user);
        }

        public Task<IdentityResult> ConfirmEmailAsync(ApplicationUser user, string token)
        {
            return _userManager.ConfirmEmailAsync(user, token);
        }

        public Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword)
        {
            return _userManager.ResetPasswordAsync(user, token, newPassword);
        }

        public async Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                // Return a structured identity error instead of throwing for predictable UI handling.
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "UserNotFound",
                    Description = "Unable to find the current account."
                });
            }

            return await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        }

        public Task RefreshSignInAsync(ApplicationUser user)
        {
            return _signInManager.RefreshSignInAsync(user);
        }

        public Task<bool> IsEmailConfirmedAsync(ApplicationUser user)
        {
            return _userManager.IsEmailConfirmedAsync(user);
        }

        public Task<bool> SendEmailConfirmationAsync(ApplicationUser user, string confirmationLink)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return Task.FromResult(false);
            }

            // Email body is intentionally short; link generation happens in controller flow.
            var subject = "Confirm your Vizora account";
            var htmlBody =
                $"Please confirm your account by <a href=\"{confirmationLink}\">clicking here</a>.";

            return _accountEmailService.SendEmailAsync(user.Email, subject, htmlBody);
        }

        public Task<bool> SendPasswordResetEmailAsync(ApplicationUser user, string resetLink)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return Task.FromResult(false);
            }

            var subject = "Reset your Vizora password";
            var htmlBody =
                $"Reset your password by <a href=\"{resetLink}\">clicking here</a>.";

            return _accountEmailService.SendEmailAsync(user.Email, subject, htmlBody);
        }
    }
}
