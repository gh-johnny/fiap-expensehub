using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Api.Documentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>
/// Checks the published OpenAPI contract. The host runs in memory (TestServer): no sockets,
/// database or broker, only the real controllers and the document transformers.
/// </summary>
[TestClass]
public sealed class OpenApiDocumentTests
{
    private static readonly string[] _mutationStatuses = { "400", "401", "403", "404", "409", "500" };
    private static readonly string[] _states = { "Draft", "Submitted", "Approved", "Rejected", "Paid" };
    private static JsonElement _document;

    /// <summary>Builds the in-memory host once and downloads the document.</summary>
    /// <param name="testContext">The MSTest context.</param>
    /// <returns>A task representing the download.</returns>
    [ClassInitialize]
    public static async Task LoadDocumentAsync(TestContext testContext)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi(ApiDocumentation.Configure);
        builder.Services.AddControllers().AddApplicationPart(typeof(ExpensesController).Assembly);
        builder.Services.AddAuthorization();
        await using WebApplication app = builder.Build();
        app.MapControllers();
        app.MapOpenApi();
        app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
        app.MapPost("/register", () => Results.Ok());
        app.MapGet("/api/expenses/public-probe", () => Results.Ok()).RequireAuthorization().AllowAnonymous();
        await app.StartAsync(testContext.CancellationToken);
        using HttpClient client = app.GetTestClient();
        string json = await client.GetStringAsync("/openapi/v1.json", testContext.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(json);
        _document = document.RootElement.Clone();
        await app.StopAsync(testContext.CancellationToken);
    }

    /// <summary>The document identifies the API and declares bearer authentication.</summary>
    [TestMethod]
    public void DeclaresInfoAndBearerScheme()
    {
        Assert.AreEqual("ExpenseHub reimbursement API", _document.GetProperty("info").GetProperty("title").GetString());
        JsonElement bearer = _document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.AreEqual("bearer", bearer.GetProperty("scheme").GetString());
    }

    /// <summary>A host without schemas still receives the components section and the bearer scheme.</summary>
    /// <returns>A task representing the in-memory request.</returns>
    [TestMethod]
    public async Task DocumentWithoutSchemasStillDeclaresBearer()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi(ApiDocumentation.Configure);
        await using WebApplication app = builder.Build();
        app.MapOpenApi();
        app.MapGet("/health", () => "ok");
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync(new System.Uri("/openapi/v1.json", System.UriKind.Relative)));
        Assert.IsTrue(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        await app.StopAsync();
    }

    /// <summary>Each route family receives its tag.</summary>
    /// <param name="path">The OpenAPI path.</param>
    /// <param name="method">The lower-case HTTP method.</param>
    /// <param name="tag">The expected tag.</param>
    [TestMethod]
    [DataRow("/api/expenses", "post", "Expenses")]
    [DataRow("/api/expenses/{id}/history", "get", "History")]
    [DataRow("/api/admin/users", "get", "Admin")]
    [DataRow("/api/reports", "post", "Reports")]
    [DataRow("/api/reports/{id}/excel", "get", "Reports")]
    [DataRow("/health", "get", "System")]
    [DataRow("/register", "post", "Identity")]
    public void AssignsTags(string path, string method, string tag)
        => Assert.AreEqual(tag, Operation(path, method).GetProperty("tags")[0].GetString());

    /// <summary>Protected mutations on a resource document every ProblemDetails status.</summary>
    [TestMethod]
    public void ProtectedMutationListsProblemStatuses()
    {
        JsonElement approve = Operation("/api/expenses/{id}/approve", "post");
        string[] statuses = approve.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToArray();
        CollectionAssert.IsSubsetOf(_mutationStatuses, statuses);
        Assert.AreEqual(1, approve.GetProperty("security").GetArrayLength());
    }

    /// <summary>The report request documents 503 (unconfirmed publication) and the download documents 409 (not Completed).</summary>
    [TestMethod]
    public void ReportRoutesDocumentTheirSpecificStatuses()
    {
        Assert.IsTrue(Operation("/api/reports", "post").GetProperty("responses").TryGetProperty("503", out _));
        Assert.IsTrue(Operation("/api/reports", "post").GetProperty("responses").TryGetProperty("202", out _));
        JsonElement excel = Operation("/api/reports/{id}/excel", "get").GetProperty("responses");
        Assert.IsTrue(excel.TryGetProperty("409", out _));
        Assert.IsTrue(excel.TryGetProperty("404", out _));
        Assert.IsTrue(excel.GetProperty("200").GetProperty("content")
            .TryGetProperty("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", out _));
        Assert.IsFalse(Operation("/api/reports/{id}", "get").GetProperty("responses").TryGetProperty("409", out _));
        JsonElement examples = Body("/api/reports", "post").GetProperty("examples");
        Assert.AreEqual("Mine", examples.GetProperty("mine").GetProperty("value").GetProperty("scope").GetString());
        Assert.IsTrue(examples.TryGetProperty("invalid", out _));
    }

    /// <summary>Reads document 404 but never 400 or 409.</summary>
    [TestMethod]
    public void ProtectedReadOmitsMutationStatuses()
    {
        string[] statuses = Operation("/api/expenses/{id}", "get").GetProperty("responses").EnumerateObject()
            .Select(response => response.Name).ToArray();
        CollectionAssert.Contains(statuses, "404");
        CollectionAssert.DoesNotContain(statuses, "400");
        CollectionAssert.DoesNotContain(statuses, "409");
    }

    /// <summary>Anonymous endpoints carry no security requirement, even when authorization metadata is present.</summary>
    /// <param name="path">The OpenAPI path.</param>
    [TestMethod]
    [DataRow("/health")]
    [DataRow("/api/expenses/public-probe")]
    public void AnonymousEndpointsHaveNoSecurity(string path)
        => Assert.AreEqual(0, Operation(path, "get").GetProperty("security").GetArrayLength());

    /// <summary>Draft creation and edition publish a valid and an invalid example; rejection publishes a reason.</summary>
    [TestMethod]
    public void PublishesRequestExamples()
    {
        foreach ((string path, string method) in new[] { ("/api/expenses", "post"), ("/api/expenses/{id}", "put") })
        {
            JsonElement examples = Body(path, method).GetProperty("examples");
            Assert.IsTrue(examples.TryGetProperty("valid", out _));
            Assert.IsTrue(examples.TryGetProperty("invalid", out _));
        }

        Assert.AreEqual("Receipt is missing", Body("/api/expenses/{id}/reject", "post").GetProperty("example").GetProperty("reason").GetString());
        Assert.IsFalse(Body("/api/admin/users/{id}/roles", "put").TryGetProperty("examples", out _));
    }

    /// <summary>Schemas expose stable error fields, decimal amounts and the closed set of states.</summary>
    [TestMethod]
    public void DescribesSchemas()
    {
        JsonElement schemas = _document.GetProperty("components").GetProperty("schemas");
        JsonElement problem = Operation("/api/expenses/{id}/approve", "post").GetProperty("responses").GetProperty("409")
            .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("properties");
        Assert.IsTrue(problem.TryGetProperty("code", out _));
        Assert.IsTrue(problem.TryGetProperty("traceId", out _));
        JsonElement expense = schemas.GetProperty("ExpenseResponse").GetProperty("properties");
        Assert.AreEqual("decimal", expense.GetProperty("amount").GetProperty("format").GetString());
        string[] states = expense.GetProperty("status").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray();
        CollectionAssert.AreEqual(_states, states);
        Assert.AreEqual(5, schemas.GetProperty("ExpenseHistoryResponse").GetProperty("properties")
            .GetProperty("newStatus").GetProperty("enum").GetArrayLength());
    }

    private static JsonElement Operation(string path, string method)
        => _document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static JsonElement Body(string path, string method)
        => Operation(path, method).GetProperty("requestBody").GetProperty("content").GetProperty("application/json");
}
