using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ExpenseHub.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks the shared ProblemDetails customization: every native error carries a stable code and the traceId.</summary>
[TestClass]
public sealed class ApiHttpPipelineTests
{
    /// <summary>Each native status maps to its stable code; anything else is the generic server code.</summary>
    /// <param name="status">The native status written without a code.</param>
    /// <param name="code">The expected stable code.</param>
    /// <returns>A task representing the write.</returns>
    [TestMethod]
    [DataRow(400, "request.invalid")]
    [DataRow(401, "auth.required")]
    [DataRow(403, "auth.forbidden")]
    [DataRow(404, "resource.not_found")]
    [DataRow(405, "server.unexpected")]
    [DataRow(500, "server.unexpected")]
    public async Task NativeStatusesReceiveAStableCode(int status, string code)
    {
        JsonElement problem = await WriteAsync(new ProblemDetails { Status = status });
        Assert.AreEqual(code, problem.GetProperty("code").GetString());
        Assert.AreEqual("trace-1", problem.GetProperty("traceId").GetString());
    }

    /// <summary>A code already chosen by the application is never overwritten by the generic mapping.</summary>
    /// <returns>A task representing the write.</returns>
    [TestMethod]
    public async Task ExplicitCodeIsPreserved()
    {
        var details = new ProblemDetails { Status = 404 };
        details.Extensions["code"] = "report.not_found";
        JsonElement problem = await WriteAsync(details);
        Assert.AreEqual("report.not_found", problem.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> WriteAsync(ProblemDetails details)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddApiControllers();
        await using ServiceProvider provider = services.BuildServiceProvider();
        var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "trace-1" };
        context.Response.Body = body;
        context.Response.StatusCode = details.Status!.Value;
        bool written = await provider.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details });
        Assert.IsTrue(written);
        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        return document.RootElement.Clone();
    }
}
