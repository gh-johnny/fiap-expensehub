using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Api.Infrastructure;

internal sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var expected = exception as ApiProblemException;
        int status = expected?.StatusCode ?? StatusCodes.Status500InternalServerError;
        string code = expected?.Code ?? "server.unexpected";
        if (expected is null)
        {
            _logger.LogError(exception, "Unexpected API failure {TraceId}", httpContext.TraceIdentifier);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = expected?.Message ?? "An unexpected server error occurred.",
            Type = "urn:expensehub:problem:" + code,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
