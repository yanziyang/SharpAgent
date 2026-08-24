using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAgent.Api.Composition;
using SharpAgent.Api.Endpoints;
using SharpAgent.Api.ErrorHandling;
using SharpAgent.Api.Startup;
using SharpAgent.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharpAgentProblemHandler>();
builder.Services.ConfigureHttpJsonOptions(static options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});
builder.Services.AddApplicationServices();
builder.Services.AddSharpAgentServices(builder.Configuration);
builder.Services.AddHostedService<PersistenceStartupService>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapApiEndpoints();

app.Run();

/// <summary>Composition root; exercised by integration tests via WebApplicationFactory.</summary>
public partial class Program
{
}
