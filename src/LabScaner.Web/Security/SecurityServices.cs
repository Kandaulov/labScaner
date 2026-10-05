using System.Net;
using System.Threading.RateLimiting;
using LabScaner.Core.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace LabScaner.Web.Security;

internal static class SecurityServices
{
    public const string AdminPolicy = "Admin";
    public const string LoginRateLimit = "login";

    public static IServiceCollection AddWebSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTeacher, HttpCurrentTeacher>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "labscaner.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));

        // Ключи шифрования cookie и токенов подключений — в каталоге, который в Docker монтируется томом.
        var keysPath = configuration["DataProtection:KeysPath"] ?? Path.Combine(AppContext.BaseDirectory, "dataprotection-keys");
        services.AddDataProtection()
            .SetApplicationName("labScaner")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        // Ограничение попыток входа с одного адреса (ARCHITECTURE.md, раздел 7) — в дополнение к блокировке учётной записи.
        var perMinute = configuration.GetValue("Security:LoginRequestsPerMinute", 20);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        // За Caddy адрес клиента и схема приходят в X-Forwarded-*; доверяем только частным сетям (Docker).
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
            options.KnownProxies.Add(IPAddress.Loopback);
        });

        return services;
    }
}
