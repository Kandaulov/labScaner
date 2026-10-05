using System.Reflection;
using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages;

public sealed class IndexModel(UserManager<AppUser> userManager) : PageModel
{
    public string Version { get; } =
        typeof(IndexModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";

    public string DisplayName { get; private set; } = string.Empty;

    public IReadOnlyList<string> RoleTitles { get; private set; } = [];

    public bool IsAdmin { get; private set; }

    public async Task OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        DisplayName = user?.DisplayName ?? User.Identity?.Name ?? string.Empty;
        RoleTitles = [.. new[] { Roles.Teacher, Roles.Admin }.Where(User.IsInRole).Select(Roles.Title)];
        IsAdmin = User.IsInRole(Roles.Admin);
    }
}
