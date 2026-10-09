using System.Globalization;
using System.Security.Claims;
using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages.Admin;

public sealed class UsersModel(UserAdministration admin) : PageModel
{
    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    /// <summary>Результат последнего действия: сообщение и, если выдан, временный пароль.</summary>
    public AdminResult? Result { get; private set; }

    public string? ResultTitle { get; private set; }

    [BindProperty]
    public NewUserInput NewUser { get; set; } = new();

    public int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    public async Task OnGetAsync() => Users = await admin.ListAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        Result = await admin.CreateTeacherAsync(NewUser.Login, NewUser.DisplayName, NewUser.IsAdmin);
        ResultTitle = Result.Succeeded ? $"Добавлен пользователь {Result.Login}" : "Пользователь не добавлен";
        if (Result.Succeeded)
        {
            NewUser = new NewUserInput();
            ModelState.Clear();
        }

        Users = await admin.ListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostResetAsync(int id)
    {
        Result = await admin.ResetPasswordAsync(id);
        ResultTitle = Result.Succeeded ? $"Новый пароль для {Result.Login}" : "Пароль не сброшен";
        Users = await admin.ListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostBlockAsync(int id, bool blocked)
    {
        Result = await admin.SetBlockedAsync(id, blocked, CurrentUserId);
        ResultTitle = Result.Succeeded ? (blocked ? $"{Result.Login} заблокирован" : $"{Result.Login} разблокирован") : "Не выполнено";
        Users = await admin.ListAsync();
        return Page();
    }

    public sealed class NewUserInput
    {
        public string Login { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsAdmin { get; set; }
    }
}
