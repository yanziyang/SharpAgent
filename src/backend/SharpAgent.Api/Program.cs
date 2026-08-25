using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAgent.Api.Composition;
using SharpAgent.Api.Endpoints;
using SharpAgent.Api.ErrorHandling;
using SharpAgent.Api.Middleware;
using SharpAgent.Api.Startup;
using SharpAgent.Api.Runtime;
using SharpAgent.Application;
using SharpAgent.Infrastructure.Setup;
using SharpAgent.Providers;
using SharpAgent.Runtime.Maf;

var builder = WebApplication.CreateBuilder(args);

var localConfigurationDisabled = string.Equals(
    Environment.GetEnvironmentVariable("SHARPAGENT_DISABLE_LOCAL_CONFIGURATION"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!localConfigurationDisabled)
{
    var localConfigurationPath = FindConfigurationUpwards(
        Directory.GetCurrentDirectory(),
        "appsettings.Local.json");
    var localConfigurationDirectory = Path.GetDirectoryName(localConfigurationPath)!;
    builder.Configuration.AddJsonFile(
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(localConfigurationDirectory),
        Path.GetFileName(localConfigurationPath),
        optional: true,
        reloadOnChange: false);

    // The local file is a server-only convenience. Preserve the existing provider
    // contract by resolving the credential from the API process environment; never
    // expose this value through a DTO, event, log, or browser request.
    var localOpenCodeGoApiKey = builder.Configuration["OpenCodeGo:ApiKey"];
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenCodeGoCatalogOptions.SecretReference))
        && !string.IsNullOrWhiteSpace(localOpenCodeGoApiKey))
    {
        Environment.SetEnvironmentVariable(OpenCodeGoCatalogOptions.SecretReference, localOpenCodeGoApiKey);
    }
}

// Troubleshooting logging is server-only. Local configuration can disable it
// without exposing a logging switch, provider payload, or environment value to
// the browser.
if (!builder.Configuration.GetValue("Troubleshooting:LoggingEnabled", true))
{
    builder.Logging.ClearProviders();
}
else
{
    // Keep local failures observable in the console without relying on the
    // Windows Event Log source, which may be unavailable to a non-admin local
    // operator and can otherwise terminate the run coordinator while it logs
    // a provider failure.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharpAgentProblemHandler>();
builder.Services.ConfigureHttpJsonOptions(static options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});
builder.Services.AddApplicationServices();
var localDemoEnabled = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>(LocalDemoOptions.EnabledKey);
builder.Services.AddProviderAdapters(localDemoEnabled);
builder.Services.AddMafRuntime(builder.Configuration);
builder.Services.AddSharpAgentServices(builder.Configuration);
builder.Services.AddHostedService<PersistenceStartupService>();
builder.Services.AddHostedService<LocalDemoCatalogStartupService>();
builder.Services.AddHostedService<OpenCodeGoCatalogStartupService>();
builder.Services.AddRunCoordinator();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestObservabilityMiddleware>();

app.MapApiEndpoints();

app.Run();

static string FindConfigurationUpwards(string startDirectory, string fileName)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return Path.Combine(startDirectory, fileName);
}

/// <summary>Composition root; exercised by integration tests via WebApplicationFactory.</summary>
public partial class Program
{
}
