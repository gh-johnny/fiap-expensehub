using System;
using System.Threading.Tasks;
using ExpenseHub.Core.Messaging.RabbitMq;
using ExpenseHub.Core.Persistence;
using ExpenseHub.Core.Reports;
using ExpenseHub.Worker.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExpenseHub.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(
            builder.Configuration.GetConnectionString("ExpenseHub") ?? "Data Source=expensehub.db"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.Configure<ReportProcessingOptions>(builder.Configuration.GetSection(ReportProcessingOptions.SectionName));
        builder.Services.AddSingleton(services => new RabbitMqConnectionProvider(
            services.GetRequiredService<IOptions<RabbitMqOptions>>(), "expensehub-worker"));
        builder.Services.AddScoped<IReportStore, EfReportStore>();
        builder.Services.AddScoped<ReportProcessor>();
        builder.Services.AddHostedService<ReportConsumer>();

        using IHost host = builder.Build();
        await host.RunAsync();
    }
}
