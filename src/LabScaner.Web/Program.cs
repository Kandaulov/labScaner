using LabScaner.Infrastructure;
using LabScaner.Jobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJobs();
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapHealthChecks("/health");
app.MapRazorPages();

await app.RunAsync();

/// <summary>Точка входа; открыта для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program;
