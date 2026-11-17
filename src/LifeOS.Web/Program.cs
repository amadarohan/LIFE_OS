using LifeOS.Application;
using LifeOS.Application.UseCases.Settings;
using LifeOS.Infrastructure;
using LifeOS.Infrastructure.Persistence;
using LifeOS.Web.Components;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "lifeos-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var app = builder.Build();

// Apply migrations and seed defaults on startup. EnsureCreated is deliberately not used: the
// schema is owned by migrations so upgrades are reproducible.
await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
        await db.Database.MigrateAsync();

        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await settings.EnsureDefaultsAsync();

        logger.LogInformation("Database is up to date and default settings are present.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Startup failed while preparing the database.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
