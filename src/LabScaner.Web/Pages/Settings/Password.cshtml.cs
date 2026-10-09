using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages.Settings;

public sealed class PasswordModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, UserAdministration admin) : PageModel
{
    [BindProperty]
    public string Current { get; set; } = string.Empty;

    [BindProperty]
    public string New { get; set; } = string.Empty;

    [BindProperty]
    public string Repeat { get; set; } = string.Empty;

    public bool Forced => User.HasClaim(c => c.Type == AppClaims.MustChangePassword);

    public string? Error { get; private set; }

    public bool Done { get; private set; }

    public void OnGet(bool done = false) => Done = done;

    public async Task<IActionResult> OnPostAsync()
    {
        if (New != Repeat)
        {
            Error = "Новый пароль и повтор не совпадают.";
            return Page();
        }

        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await admin.ChangeOwnPasswordAsync(user, Current, New);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        // Перевыпустить cookie: снять отметку «сменить пароль».
        await signIn.RefreshSignInAsync(user);
        return RedirectToPage(new { done = true });
    }
}
