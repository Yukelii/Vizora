using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Vizora.Models;
using Vizora.Services;

namespace Vizora.Controllers
{
    public class AuthController : Controller
    {
        private const string PendingConfirmationEmailTempDataKey = "PendingConfirmationEmail";
        private const string ConfirmationNoticeTempDataKey = "ConfirmationNotice";

        private readonly IAccountLifecycleService _accountLifecycleService;
        private readonly IUserContextService _userContextService;

        public AuthController(
            IAccountLifecycleService accountLifecycleService,
            IUserContextService userContextService)
        {
            _accountLifecycleService = accountLifecycleService;
            _userContextService = userContextService;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToDashboard();
            }

            return View(new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedUserName = model.UserName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUserName))
            {
                ModelState.AddModelError(nameof(model.UserName), "Username is required.");
                return View(model);
            }

            var signInResult = await _accountLifecycleService.PasswordSignInAsync(
                normalizedUserName,
                model.Password,
                model.RememberMe);

            if (signInResult.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToDashboard();
            }

            if (signInResult.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Your account is temporarily locked due to multiple failed login attempts.");
                return View(model);
            }

            if (signInResult.IsNotAllowed)
            {
                // If credentials are correct but sign-in is blocked, route unconfirmed users
                // to the confirmation page without exposing account state broadly.
                var user = await _accountLifecycleService.FindByUserNameAsync(normalizedUserName);
                if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                {
                    var emailConfirmed = await _accountLifecycleService.IsEmailConfirmedAsync(user);
                    if (!emailConfirmed)
                    {
                        SetPendingConfirmationEmail(user.Email);
                        SetConfirmationNotice("Your email is not confirmed yet. Check your inbox for a confirmation link.");
                        return RedirectToAction(nameof(EmailConfirmationPending));
                    }
                }

                ModelState.AddModelError(string.Empty, "Sign-in is currently not allowed for this account.");
                return View(model);
            }

            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Register(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToDashboard();
            }

            return View(new RegisterViewModel
            {
                ReturnUrl = returnUrl
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userName = model.UserName.Trim();
            var email = model.Email.Trim();

            // Keep uniqueness validation server-side to avoid duplicate account creation.
            var existingUserName = await _accountLifecycleService.FindByUserNameAsync(userName);
            if (existingUserName != null)
            {
                ModelState.AddModelError(nameof(model.UserName), "Username is already taken.");
                return View(model);
            }

            var existingEmail = await _accountLifecycleService.FindByEmailAsync(email);
            if (existingEmail != null)
            {
                ModelState.AddModelError(nameof(model.Email), "Email is already in use.");
                return View(model);
            }

            var registrationResult = await _accountLifecycleService.RegisterAsync(userName, email, model.Password);
            if (!registrationResult.IdentityResult.Succeeded)
            {
                // Return identity validation errors without persisting auth session state.
                foreach (var error in registrationResult.IdentityResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            if (registrationResult.User == null || string.IsNullOrWhiteSpace(registrationResult.User.Email))
            {
                ModelState.AddModelError(string.Empty, "Registration failed. Please try again.");
                return View(model);
            }

            await SendEmailConfirmationAsync(registrationResult.User);
            SetPendingConfirmationEmail(registrationResult.User.Email);
            SetConfirmationNotice("Account created. Check your email to confirm your account.");

            return RedirectToAction(nameof(EmailConfirmationPending));
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            {
                ViewData["StatusMessage"] = "Invalid email confirmation request.";
                return View();
            }

            var user = await _accountLifecycleService.FindByIdAsync(userId);
            if (user == null)
            {
                ViewData["StatusMessage"] = "Unable to load user for email confirmation.";
                return View();
            }

            string decodedToken;
            try
            {
                // Tokens are URL-safe encoded in links and must be decoded before Identity can consume them.
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                ViewData["StatusMessage"] = "Invalid email confirmation token.";
                return View();
            }

            var result = await _accountLifecycleService.ConfirmEmailAsync(user, decodedToken);
            ViewData["StatusMessage"] = result.Succeeded
                ? "Thank you for confirming your email."
                : "Email confirmation failed.";
            if (result.Succeeded)
            {
                TempData.Remove(PendingConfirmationEmailTempDataKey);
            }

            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult EmailConfirmationPending()
        {
            var pendingEmail = GetPendingConfirmationEmail();
            if (string.IsNullOrWhiteSpace(pendingEmail))
            {
                return RedirectToAction(nameof(Login));
            }

            if (TempData.TryGetValue(ConfirmationNoticeTempDataKey, out var statusMessage) && statusMessage is string message)
            {
                ViewData["StatusMessage"] = message;
            }

            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmationEmail()
        {
            var pendingEmail = GetPendingConfirmationEmail();
            if (string.IsNullOrWhiteSpace(pendingEmail))
            {
                return RedirectToAction(nameof(Login));
            }

            var normalizedEmail = pendingEmail.Trim();
            var user = await _accountLifecycleService.FindByEmailAsync(normalizedEmail);
            if (user != null)
            {
                // Only resend while the email is still unconfirmed.
                var emailConfirmed = await _accountLifecycleService.IsEmailConfirmedAsync(user);
                if (!emailConfirmed)
                {
                    await SendEmailConfirmationAsync(user);
                }
            }

            // Keep the pending email in TempData for follow-up resend attempts.
            SetPendingConfirmationEmail(normalizedEmail);
            SetConfirmationNotice("If the account exists and still needs confirmation, a new confirmation email has been sent.");

            return RedirectToAction(nameof(EmailConfirmationPending));
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim();
            var user = await _accountLifecycleService.FindByEmailAsync(normalizedEmail);

            if (user != null)
            {
                // Keep response generic to avoid email-enumeration leaks.
                var resetToken = await _accountLifecycleService.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetToken));

                var resetUrl = Url.Action(
                    nameof(ResetPassword),
                    "Auth",
                    new { code = encodedToken, email = user.Email },
                    Request.Scheme);

                if (!string.IsNullOrWhiteSpace(resetUrl))
                {
                    await _accountLifecycleService.SendPasswordResetEmailAsync(user, resetUrl);
                }
            }

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ResetPassword(string? code = null, string? email = null)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(email))
            {
                return BadRequest("A code and email are required for password reset.");
            }

            string decodedToken;
            try
            {
                // Reset password links carry an encoded token to keep it URL-safe.
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return BadRequest("Invalid password reset token.");
            }

            return View(new ResetPasswordViewModel
            {
                Email = email,
                Token = decodedToken
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim();
            var user = await _accountLifecycleService.FindByEmailAsync(normalizedEmail);
            if (user == null)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            var result = await _accountLifecycleService.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = _userContextService.GetRequiredUserId();
            var result = await _accountLifecycleService.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            var user = await _accountLifecycleService.FindByIdAsync(userId);
            if (user != null)
            {
                await _accountLifecycleService.RefreshSignInAsync(user);
            }

            return RedirectToAction(nameof(ChangePasswordConfirmation));
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePasswordConfirmation()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _accountLifecycleService.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        private async Task SendEmailConfirmationAsync(ApplicationUser user)
        {
            var emailConfirmationToken = await _accountLifecycleService.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(emailConfirmationToken));

            // Generate an absolute callback URL for external email clients.
            var callbackUrl = Url.Action(
                nameof(ConfirmEmail),
                "Auth",
                new { userId = user.Id, code = encodedToken },
                Request.Scheme);

            if (!string.IsNullOrWhiteSpace(callbackUrl))
            {
                await _accountLifecycleService.SendEmailConfirmationAsync(user, callbackUrl);
            }
        }

        private IActionResult RedirectToDashboard()
        {
            return RedirectToAction("Index", "Dashboard");
        }

        private string? GetPendingConfirmationEmail()
        {
            return TempData.Peek(PendingConfirmationEmailTempDataKey) as string;
        }

        private void SetPendingConfirmationEmail(string email)
        {
            TempData[PendingConfirmationEmailTempDataKey] = email;
        }

        private void SetConfirmationNotice(string message)
        {
            TempData[ConfirmationNoticeTempDataKey] = message;
        }
    }
}
