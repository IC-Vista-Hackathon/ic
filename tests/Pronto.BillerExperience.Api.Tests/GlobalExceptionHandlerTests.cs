using System.Text.Json;
using Pronto.BillerExperience.Api.Infrastructure;
using Pronto.BillerExperience.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pronto.BillerExperience.Api.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "resource_not_found")]
    [InlineData(typeof(ArgumentException), StatusCodes.Status400BadRequest, "invalid_request")]
    [InlineData(typeof(ConcurrencyException), StatusCodes.Status409Conflict, "concurrent_update")]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError, "unexpected_error")]
    public async Task MapsExceptionTypesToProblemDetails(Type exceptionType, int expectedStatus, string expectedCode)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        body.Position = 0;
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("trace_id", out _));
    }

    [Fact]
    public async Task InternalErrorsDoNotLeakExceptionDetail()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await handler.TryHandleAsync(
            context, new InvalidOperationException("secret connection string"), CancellationToken.None);

        body.Position = 0;
        using var problem = JsonDocument.Parse(body);
        Assert.DoesNotContain(
            "secret", problem.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
