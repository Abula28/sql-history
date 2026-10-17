using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace SqlHistory.Hackathon.Api;

/// <summary>
/// Requires a custom header on state-changing requests. Browsers only send custom headers
/// cross-origin after a CORS preflight, which fails for any origin not in Cors:AllowedOrigins,
/// so a malicious page cannot trigger scans or restores through the user's browser.
/// </summary>
public sealed class RequireClientHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-SqlHistory-Client";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.ContainsKey(HeaderName))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                $"State-changing requests must include the {HeaderName} header.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return next(context);
    }
}

/// <summary>
/// Turns unhandled exceptions into problem details without stack traces or configuration.
/// Source-database failures become 503 so the UI can say the database is unreachable.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, detail) = exception switch
        {
            NpgsqlException ex => (StatusCodes.Status503ServiceUnavailable,
                $"The PostgreSQL source database is unavailable: {ex.Message}"),
            _ => (StatusCodes.Status500InternalServerError,
                "Unexpected server error. See the API log for details.")
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status, Detail = detail }
        });
    }
}
