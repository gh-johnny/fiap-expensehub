using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Infrastructure;

internal sealed class RequestAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestAuditMiddleware> _logger;

    public RequestAuditMiddleware(RequestDelegate next, ILogger<RequestAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        long started = Stopwatch.GetTimestamp();
        int status = 500;
        try
        {
            await _next(context);
            status = context.Response.StatusCode;
        }
        catch (ApiProblemException exception)
        {
            status = exception.StatusCode;
            throw;
        }
        finally
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "HTTP {Method} {Path} {ResourceId} {ActorId} {TraceId} {StatusCode} {ElapsedMilliseconds}",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Request.RouteValues["id"],
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    context.TraceIdentifier,
                    status,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }
}
