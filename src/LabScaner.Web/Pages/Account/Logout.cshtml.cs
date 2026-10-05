using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages.Account;

/// <summary>Выход — только POST с антифорджери-токеном, чтобы ссылка не могла разлогинить.</summary>
public sealed class LogoutModel(SignInManager<AppUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
