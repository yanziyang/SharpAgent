using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharpAgent.Api.ErrorHandling;
using SharpAgent.Application.Common;
using SharpAgent.Domain.Common;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.ErrorHandling;

/// <summary>Direct unit coverage of the problem-details mapping table.</summary>
public sealed class SharpAgentProblemHandlerTests
{
    public static TheoryData<Exception, int, string> MappedCases() => new()
    {
        { new NotFoundException("session", "ses_x"), 404, "not_found" },
        { new ConflictException("session_active", "busy"), 409, "session_active" },
        {
            new ValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["task"] = ["required"],
            }),
            400,
            "validation_error"
        },
        {
            new InvalidStateTransitionException("session", "draft", "completed"),
            409,
            "invalid_transition"
        },
        { new DbUpdateConcurrencyException("conflict"), 409, "concurrency_conflict" },
    };

    [Theory]
    [MemberData(nameof(MappedCases))]
    public async Task Known_exceptions_map_to_stable_problem_codes(
        Exception exception,
        int expectedStatus,
        string expectedCode)
    {
        var (response, handled) = await HandleAsync(exception);

        Assert.True(handled);
        Assert.Equal(expectedStatus, response.StatusCode);

        var body = ReadJson(response.Body);
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Unknown_exceptions_are_left_to_the_default_handler()
    {
        var (_, handled) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.False(handled);
    }

    private static async Task<(HttpResponse Response, bool Handled)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        var handler = new SharpAgentProblemHandler(NullLogger<SharpAgentProblemHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        bodyStream.Position = 0;
        return (context.Response, handled);
    }

    private static JsonElement ReadJson(Stream body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}

