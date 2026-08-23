using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAgent.Api.Composition;
using SharpAgent.Api.Endpoints;
using SharpAgent.Application.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(static options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});
builder.Services.AddSharpAgentServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.MapApiEndpoints();

app.Run();

/// <summary>Composition root; exercised by integration tests via WebApplicationFactory.</summary>
public partial class Program
{
}
