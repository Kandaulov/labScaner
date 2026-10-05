using LabScaner.Infrastructure;
using LabScaner.Infrastructure.Identity;
using LabScaner.Infrastructure.Persistence;
using LabScaner.Jobs;
using LabScaner.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJobs();
builder.Services.AddWebSecurity(builder.Configuration);
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", SecurityServices.AdminPolicy);
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapRazorPages();

await app.Services.MigrateDatabaseAsync();
await app.Services.SeedIdentityAsync();
await app.RunAsync();

/// <summary>Точка входа; открыта для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program;
