using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseHub.Api.Infrastructure;

/// <summary>
/// The HTTP error contract and middleware order, shared by Program and the in-memory HTTP tests so that both run
/// the same pipeline: ProblemDetails with a stable code and traceId for every error, including native 401/403/404.
/// </summary>
internal static class ApiHttpPipeline
{
    /// <summary>Registers controllers, the exception handler and the ProblemDetails customization.</summary>
    /// <param name="services">The application services.</param>
    /// <returns>The MVC builder, so callers can add application parts.</returns>
    public static IMvcBuilder AddApiControllers(this IServiceCollection services)
    {
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
            {
                400 => "request.invalid",
                401 => "auth.required",
                403 => "auth.forbidden",
                404 => "resource.not_found",
                _ => "server.unexpected",
            });
        });
        return services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Status = 400,
                    Title = "The request is invalid.",
                    Instance = context.HttpContext.Request.Path,
                };
                problem.Extensions["code"] = "request.invalid";
                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                return new BadRequestObjectResult(problem);
            };
        });
    }

    /// <summary>Adds error handling, authentication, request auditing and authorization, in that order.</summary>
    /// <param name="app">The application pipeline.</param>
    public static void UseApiPipeline(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
        {
            var writer = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await writer.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails { Status = context.HttpContext.Response.StatusCode },
            });
        });
        app.UseAuthentication();
        app.UseMiddleware<RequestAuditMiddleware>();
        app.UseAuthorization();
    }
}
