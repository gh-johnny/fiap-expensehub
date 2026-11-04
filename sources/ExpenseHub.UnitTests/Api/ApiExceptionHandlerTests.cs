using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks the ProblemDetails contract for expected and unexpected failures.</summary>
[TestClass]
public sealed class ApiExceptionHandlerTests
{
    /// <summary>A domain problem keeps its status and stable code and is not logged as an error.</summary>
    [TestMethod]
    public async Task ExpectedProblemKeepsStatusAndCode()
    {
        var logger = new ListLogger<ApiExceptionHandler>();
        (HttpContext context, JsonElement body) = await HandleAsync(logger, new ApiProblemException(409, "expense.invalid_transition", "Conflict."));
        Assert.AreEqual(409, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        Assert.AreEqual("expense.invalid_transition", body.GetProperty("code").GetString());
        Assert.AreEqual("urn:expensehub:problem:expense.invalid_transition", body.GetProperty("type").GetString());
        Assert.AreEqual("Conflict.", body.GetProperty("title").GetString());
        Assert.AreEqual("trace-1", body.GetProperty("traceId").GetString());
        Assert.IsEmpty(logger.Entries);
    }

    /// <summary>Extensions such as the persisted report id are published next to code and traceId.</summary>
    [TestMethod]
    public async Task ProblemExtensionsArePublished()
    {
        var id = Guid.NewGuid();
        var problem = new ApiProblemException(503, "report.publish_unconfirmed", "Not queued.",
            new System.Collections.Generic.Dictionary<string, object?> { ["reportId"] = id });
        (HttpContext context, JsonElement body) = await HandleAsync(new ListLogger<ApiExceptionHandler>(), problem);
        Assert.AreEqual(503, context.Response.StatusCode);
        Assert.AreEqual(id, body.GetProperty("reportId").GetGuid());
        Assert.AreEqual("report.publish_unconfirmed", body.GetProperty("code").GetString());
    }

    /// <summary>An unexpected failure answers a sanitized 500 and is logged with the trace identifier.</summary>
    [TestMethod]
    public async Task UnexpectedFailureIsSanitized()
    {
        var logger = new ListLogger<ApiExceptionHandler>();
        var failure = new InvalidOperationException("Data Source=/secret/path.db");
        (HttpContext context, JsonElement body) = await HandleAsync(logger, failure);
        Assert.AreEqual(500, context.Response.StatusCode);
        Assert.AreEqual("server.unexpected", body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body.GetRawText(), StringComparison.Ordinal);
        LogEntry entry = logger.Entries[0];
        Assert.AreEqual(LogLevel.Error, entry.Level);
        Assert.AreSame(failure, entry.Exception);
        Assert.AreEqual("trace-1", entry.Values["TraceId"]);
    }

    private static async Task<(HttpContext Context, JsonElement Body)> HandleAsync(ListLogger<ApiExceptionHandler> logger, Exception exception)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Path = "/api/expenses";
        using var stream = new MemoryStream();
        context.Response.Body = stream;
        bool handled = await new ApiExceptionHandler(logger).TryHandleAsync(context, exception, CancellationToken.None);
        Assert.IsTrue(handled);
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return (context, document.RootElement.Clone());
    }
}
