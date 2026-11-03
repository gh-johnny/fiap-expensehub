using System;
using System.Threading.Tasks;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks that every request is audited with correlation fields and the real outcome.</summary>
[TestClass]
public sealed class RequestAuditMiddlewareTests
{
    /// <summary>A completed request records method, path, resource, actor, trace and status.</summary>
    [TestMethod]
    public async Task CompletedRequestIsAudited()
    {
        var logger = new ListLogger<RequestAuditMiddleware>();
        DefaultHttpContext context = Context();
        var middleware = new RequestAuditMiddleware(next => { next.Response.StatusCode = 204; return Task.CompletedTask; }, logger);
        await middleware.InvokeAsync(context);
        LogEntry entry = logger.Entries[0];
        Assert.AreEqual("POST", entry.Values["Method"]);
        Assert.AreEqual("/api/expenses/42/submit", entry.Values["Path"]);
        Assert.AreEqual("42", entry.Values["ResourceId"]);
        Assert.AreEqual("owner", entry.Values["ActorId"]);
        Assert.AreEqual("trace-7", entry.Values["TraceId"]);
        Assert.AreEqual(204, entry.Values["StatusCode"]);
    }

    /// <summary>A domain problem is rethrown for the handler and audited with its own status.</summary>
    [TestMethod]
    public async Task DomainProblemIsAuditedAndRethrown()
    {
        var logger = new ListLogger<RequestAuditMiddleware>();
        var middleware = new RequestAuditMiddleware(_ => throw new ApiProblemException(404, "expense.not_found", "Missing."), logger);
        await Assert.ThrowsExactlyAsync<ApiProblemException>(() => middleware.InvokeAsync(Context()));
        Assert.AreEqual(404, logger.Entries[0].Values["StatusCode"]);
    }

    /// <summary>An unexpected failure is rethrown and audited as 500.</summary>
    [TestMethod]
    public async Task UnexpectedFailureIsAuditedAs500()
    {
        var logger = new ListLogger<RequestAuditMiddleware>();
        var middleware = new RequestAuditMiddleware(_ => throw new InvalidOperationException("boom"), logger);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context()));
        Assert.AreEqual(500, logger.Entries[0].Values["StatusCode"]);
    }

    /// <summary>An anonymous request is audited without an actor.</summary>
    [TestMethod]
    public async Task AnonymousRequestHasNoActor()
    {
        var logger = new ListLogger<RequestAuditMiddleware>();
        var middleware = new RequestAuditMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(new DefaultHttpContext());
        Assert.IsNull(logger.Entries[0].Values["ActorId"]);
    }

    /// <summary>Disabled Information logging produces no entry.</summary>
    [TestMethod]
    public async Task DisabledLoggingSkipsAudit()
    {
        var logger = new ListLogger<RequestAuditMiddleware> { Enabled = false };
        var middleware = new RequestAuditMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(Context());
        Assert.IsEmpty(logger.Entries);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-7", User = Principals.Authenticated("owner", RoleNames.Employee) };
        context.Request.Method = "POST";
        context.Request.Path = "/api/expenses/42/submit";
        context.Request.RouteValues["id"] = "42";
        return context;
    }
}
