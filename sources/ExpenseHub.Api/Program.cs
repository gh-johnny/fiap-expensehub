using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ExpenseHub.Api.Documentation;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Core;
using ExpenseHub.Core.Messaging.RabbitMq;
using ExpenseHub.Core.Persistence;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        string? keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            // Bearer tokens are protected with these keys; persisting them keeps sessions valid across container recreation.
            builder.Services.AddDataProtection().SetApplicationName("ExpenseHub")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        builder.Services.AddScoped<IUserRoleStore, IdentityUserRoleStore>();
        builder.Services.AddScoped<UserRoleService>();
        builder.Services.AddScoped<IExpenseStore, EfExpenseStore>();
        builder.Services.AddScoped<ExpenseService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.AddSingleton(services => new RabbitMqConnectionProvider(
            services.GetRequiredService<IOptions<RabbitMqOptions>>(), "expensehub-api"));
        builder.Services.AddScoped<IReportPublisher, RabbitMqReportPublisher>();
        builder.Services.AddScoped<IReportStore, EfReportStore>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
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
        if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
        {
            await DatabaseInitializer.MigrateAsync(app.Services, app.Lifetime.ApplicationStopping);
        }

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
        app.MapGet("/version", () => Results.Ok(new { commit = BuildInfo.Commit }))
            .WithName("GetVersion");

        await app.RunAsync();
    }
}
