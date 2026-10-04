using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabScaner.Web.Pages;

public sealed class IndexModel : PageModel
{
    public string Version { get; } =
        typeof(IndexModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";
}
