using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Urbanova.Api.Middleware;

/// <summary>
/// Phase 1 centralized error handler. Converts unhandled exceptions to RFC 7807 ProblemDetails
/// without leaking internals. Domain-specific error codes (UNSUPPORTED_FORMAT, etc.) land in
/// Phase 5+ via Urbanova.Application exceptions — this middleware already knows how to map them.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);
            await WriteProblemAsync(context, ex);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        // Never expose stack traces or internal messages in Production.
        var isDev = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        var (status, code, title) = ex switch
        {
            InvalidOperationException => (HttpStatusCode.BadRequest, "INVALID_OPERATION", "Invalid operation."),
            ArgumentException => (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid request."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized."),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
        };

        var problem = new ProblemDetails
        {
            Type = $"https://urbanova.local/errors/{code.ToLowerInvariant()}",
            Title = title,
            Status = (int)status,
            Detail = isDev ? ex.Message : "Request failed. Contact support with the traceId.",
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["code"] = code;

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(problem);
    }
}
