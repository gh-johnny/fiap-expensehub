using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExpenseHub.Api.Documentation;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Api.Persistence;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace ExpenseHub.Api;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenApi(ApiDocumentation.Configure);
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(
            builder.Configuration.GetConnectionString("ExpenseHub") ?? "Data Source=expensehub.db"));

        builder.Services.AddIdentityApiEndpoints<IdentityUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ExpenseHubDbContext>();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
            options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IUserRoleStore, IdentityUserRoleStore>();
        builder.Services.AddScoped<UserRoleService>();
        builder.Services.AddScoped<IExpenseStore, EfExpenseStore>();
        builder.Services.AddScoped<ExpenseService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
            context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
            {
                400 => "request.invalid",
                401 => "auth.required",
                403 => "auth.forbidden",
                404 => "resource.not_found",
                _ => "server.unexpected",
            });
        });
        builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
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

        WebApplication app = builder.Build();
        await IdentitySeed.InitializeAsync(app.Services, builder.Configuration, app.Lifetime.ApplicationStopping);
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
        app.MapIdentityApi<IdentityUser>();
        app.MapControllers();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference("/docs", options => options.WithTitle("ExpenseHub API").DisableAgent());
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");

        await app.RunAsync();
    }
}
