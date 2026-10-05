using LabScaner.Infrastructure.Identity;
using LabScaner.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LabScaner.Web.Pages.Account;

[EnableRateLimiting(SecurityServices.LoginRateLimit)]
public sealed class LoginModel(SignInManager<AppUser> signInManager) : PageModel
{
    public const string WrongCredentials = "Неверный логин или пароль.";
    public const string LockedOut = "Слишком много неудачных попыток. Вход временно заблокирован, попробуйте через 15 минут.";

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (string.IsNullOrWhiteSpace(Input.Login) || string.IsNullOrEmpty(Input.Password))
        {
            ErrorMessage = WrongCredentials;
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Login.Trim(), Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }

        ErrorMessage = result.IsLockedOut ? LockedOut : WrongCredentials;
        return Page();
    }

    public sealed class LoginInput
    {
        public string Login { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
