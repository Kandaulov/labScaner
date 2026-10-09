using LabScaner.Infrastructure.Identity;

namespace LabScaner.Web.Security;

/// <summary>Пользователь с временным паролем от администратора попадает на смену пароля, пока не сменит его.</summary>
internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public const string PasswordPage = "/Settings/Password";

    private static readonly string[] _allowed = [PasswordPage, "/Account/Logout", "/health", "/css/", "/js/", "/lib/"];

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (context.User.HasClaim(c => c.Type == AppClaims.MustChangePassword)
            && !_allowed.Any(a => path.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(PasswordPage);
            return Task.CompletedTask;
        }

        return next(context);
    }
}
