using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Api.Infrastructure;
using ExpenseHub.Core.Reports;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>
/// Drives the report routes through the real HTTP pipeline in memory (TestServer): routing, model binding,
/// [Authorize] attributes, the exception handler and ProblemDetails, exactly as Program wires them. Persistence and
/// broker are in-memory doubles, and a header-based test scheme replaces Identity bearer tokens.
/// </summary>
[TestClass]
public sealed class ReportsHttpTests
{
    private const string Route = "/api/reports";
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>Gets or sets the MSTest context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Starts a fresh in-memory host per test so state never leaks between scenarios.</summary>
    /// <returns>A task representing the startup.</returns>
    [TestInitialize]
    public async Task StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddApiControllers().AddApplicationPart(typeof(ReportsController).Assembly);
        builder.Services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IReportStore>(_store);
        builder.Services.AddSingleton<IReportPublisher>(_publisher);
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        builder.Services.AddScoped<ReportService>();
        _app = builder.Build();
        _app.UseApiPipeline();
        _app.MapControllers();
        await _app.StartAsync(TestContext.CancellationToken);
        _client = _app.GetTestClient();
    }

    /// <summary>Stops the in-memory host.</summary>
    /// <returns>A task representing the shutdown.</returns>
    [TestCleanup]
    public async Task StopAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Every report route refuses anonymous callers with 401 and the stable code, before any work.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="suffix">The route suffix.</param>
    [TestMethod]
    [DataRow("POST", "")]
    [DataRow("GET", "")]
    [DataRow("GET", "/8d1f3c0e-9b7a-4a51-bc0e-3f0d5e2a1b44")]
    [DataRow("GET", "/8d1f3c0e-9b7a-4a51-bc0e-3f0d5e2a1b44/excel")]
    public async Task AnonymousIsUnauthorized(string method, string suffix)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix);
        if (method == "POST")
        {
            request.Content = Json(Body("Mine"));
        }

        using HttpResponseMessage response = await _client.SendAsync(request, TestContext.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.required");
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>An impossible date is refused by model binding with 400 request.invalid, and nothing is queued.</summary>
    [TestMethod]
    public async Task ImpossibleDateIsRejectedByModelBinding()
    {
        string body = """{ "fromDate": "2026-13-45", "toDate": "2026-09-30", "scope": "Mine" }""";

        using HttpResponseMessage response = await PostAsync(body, "employee", "Employee");

        JsonElement problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.invalid");
        Assert.IsTrue(problem.TryGetProperty("errors", out _));
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>An inverted period passes binding and is refused by the service with its own code.</summary>
    [TestMethod]
    public async Task InvertedPeriodIsRejectedByTheService()
    {
        string body = """{ "fromDate": "2026-09-30", "toDate": "2026-09-01", "scope": "Mine" }""";

        using HttpResponseMessage response = await PostAsync(body, "employee", "Employee");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "report.invalid_period");
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>An Employee asking for Global gets 403 report.scope_forbidden; nothing is persisted or published.</summary>
    [TestMethod]
    public async Task EmployeeRequestingGlobalIsForbidden()
    {
        using HttpResponseMessage response = await PostAsync(Body("Global"), "employee", "Employee");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "report.scope_forbidden");
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>Admin, Approver and Finance without Employee or Auditor are refused by the [Authorize] attribute itself.</summary>
    /// <param name="roles">The comma-separated roles.</param>
    [TestMethod]
    [DataRow("Admin")]
    [DataRow("Approver")]
    [DataRow("Finance")]
    [DataRow("Admin,Approver,Finance")]
    public async Task ProfilesWithoutReportRolesAreForbiddenByThePipeline(string roles)
    {
        using HttpResponseMessage response = await PostAsync(Body("Mine"), "privileged", roles);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
        Assert.IsEmpty(_store.Reports);
    }

    /// <summary>
    /// A valid request answers 202 with Location and the Pending envelope. A requester smuggled in the body is
    /// ignored: the stored requester is the authenticated user.
    /// </summary>
    [TestMethod]
    public async Task ValidRequestIsAcceptedWithLocation()
    {
        string body = """{ "fromDate": "2026-09-01", "toDate": "2026-09-30", "scope": "Mine", "requesterId": "attacker", "ownerId": "attacker" }""";

        using HttpResponseMessage response = await PostAsync(body, "employee", "Employee");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        JsonElement envelope = await ReadJsonAsync(response);
        var id = envelope.GetProperty("id").GetGuid();
        Assert.AreEqual($"/api/reports/{id}", response.Headers.Location!.OriginalString);
        Assert.AreEqual("Pending", envelope.GetProperty("status").GetString());
        Assert.AreEqual("Mine", envelope.GetProperty("scope").GetString());
        Assert.AreEqual("2026-09-01", envelope.GetProperty("fromDate").GetString());
        Assert.AreEqual(JsonValueKind.Null, envelope.GetProperty("result").ValueKind);
        Assert.AreEqual("employee", _store.Reports[id].RequesterId);
        CollectionAssert.AreEqual(new[] { id }, _publisher.Published);
    }

    /// <summary>An Auditor may request Global and only lists its own requests afterwards.</summary>
    [TestMethod]
    public async Task AuditorRequestsGlobalAndListsOnlyItsOwn()
    {
        using HttpResponseMessage employee = await PostAsync(Body("Mine"), "employee", "Employee");
        using HttpResponseMessage auditor = await PostAsync(Body("Global"), "auditor", "Auditor");
        Assert.AreEqual(HttpStatusCode.Accepted, auditor.StatusCode);
        Guid auditorId = (await ReadJsonAsync(auditor)).GetProperty("id").GetGuid();

        using HttpResponseMessage list = await GetAsync(Route, "auditor", "Auditor");

        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        JsonElement items = await ReadJsonAsync(list);
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual(auditorId, items[0].GetProperty("id").GetGuid());
        Assert.AreEqual("Global", items[0].GetProperty("scope").GetString());
    }

    /// <summary>
    /// Detail and Excel of another user's request answer 404, even for Auditor, indistinguishable from an absent id
    /// and without any report data.
    /// </summary>
    /// <param name="suffix">The route suffix after the id.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("/excel")]
    public async Task ForeignReportIsNotFound(string suffix)
    {
        ReportRequest foreign = Stored("owner", ReportStatus.Completed);

        using HttpResponseMessage response = await GetAsync($"{Route}/{foreign.Id}{suffix}", "auditor", "Employee,Auditor");
        using HttpResponseMessage absent = await GetAsync($"{Route}/{Guid.NewGuid()}{suffix}", "auditor", "Employee,Auditor");

        JsonElement problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "report.not_found");
        JsonElement missing = await AssertProblemAsync(absent, HttpStatusCode.NotFound, "report.not_found");
        string[] fields = problem.EnumerateObject().Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(missing.EnumerateObject().Select(property => property.Name).ToArray(), fields);
        Assert.AreEqual(missing.GetProperty("title").GetString(), problem.GetProperty("title").GetString());
        Assert.AreEqual(missing.GetProperty("type").GetString(), problem.GetProperty("type").GetString());
        Assert.DoesNotContain("result", fields);
        Assert.DoesNotContain("requesterId", fields);
        Assert.DoesNotContain("owner", problem.GetRawText());
    }

    /// <summary>A non-GUID id never reaches the service: the route constraint answers 404 with the generic code.</summary>
    [TestMethod]
    public async Task NonGuidIdIsNotFoundByRouting()
    {
        using HttpResponseMessage response = await GetAsync($"{Route}/rel-001", "employee", "Employee");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "resource.not_found");
    }

    /// <summary>An unsupported method on a report route still answers ProblemDetails with a code and traceId.</summary>
    [TestMethod]
    public async Task UnsupportedMethodKeepsTheErrorContract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Route}/{Guid.NewGuid()}");
        Authenticate(request, "employee", "Employee");

        using HttpResponseMessage response = await _client.SendAsync(request, TestContext.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "server.unexpected");
    }

    /// <summary>Excel of an own request that is not Completed answers 409 and no file.</summary>
    /// <param name="status">The stored state.</param>
    [TestMethod]
    [DataRow(ReportStatus.Pending)]
    [DataRow(ReportStatus.Processing)]
    [DataRow(ReportStatus.Failed)]
    public async Task ExcelOfUnfinishedOwnReportIsConflict(ReportStatus status)
    {
        ReportRequest own = Stored("employee", status);

        using HttpResponseMessage response = await GetAsync($"{Route}/{own.Id}/excel", "employee", "Employee");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "report.not_completed");
    }

    /// <summary>Excel of an own Completed request is a real .xlsx attachment named report-{id}.xlsx.</summary>
    [TestMethod]
    public async Task ExcelOfCompletedOwnReportIsASpreadsheetAttachment()
    {
        ReportRequest own = Stored("employee", ReportStatus.Completed);

        using HttpResponseMessage response = await GetAsync($"{Route}/{own.Id}/excel", "employee", "Employee");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual($"report-{own.Id}.xlsx", response.Content.Headers.ContentDisposition.FileName);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        Assert.AreEqual((byte)'P', bytes[0]);
        Assert.AreEqual((byte)'K', bytes[1]);
    }

    /// <summary>An unconfirmed publication answers 503 with the persisted reportId; the request stays Pending.</summary>
    [TestMethod]
    public async Task UnconfirmedPublicationIsServiceUnavailableWithTheId()
    {
        _publisher.Unconfirmed = true;

        using HttpResponseMessage response = await PostAsync(Body("Mine"), "employee", "Employee");

        JsonElement problem = await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "report.publish_unconfirmed");
        var id = problem.GetProperty("reportId").GetGuid();
        Assert.AreEqual(ReportStatus.Pending, _store.Reports[id].Status);
        Assert.IsNull(response.Headers.Location);
    }

    private static string Body(string scope) => $$"""{ "fromDate": "2026-09-01", "toDate": "2026-09-30", "scope": "{{scope}}" }""";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private async Task<HttpResponseMessage> PostAsync(string body, string user, string roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = Json(body) };
        Authenticate(request, user, roles);
        return await _client.SendAsync(request, TestContext.CancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string user, string roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Authenticate(request, user, roles);
        return await _client.SendAsync(request, TestContext.CancellationToken);
    }

    private static void Authenticate(HttpRequestMessage request, string user, string roles)
    {
        request.Headers.Add(HeaderAuthenticationHandler.UserHeader, user);
        request.Headers.Add(HeaderAuthenticationHandler.RolesHeader, roles);
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.CancellationToken)).Clone();

    private async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.AreEqual((int)status, problem.GetProperty("status").GetInt32());
        Assert.AreEqual(code, problem.GetProperty("code").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    private ReportRequest Stored(string requester, ReportStatus status)
    {
        var report = new ReportRequest
        {
            RequesterId = requester,
            Scope = ReportScope.Mine,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Status = status,
            CreatedAtUtc = new FixedTimeProvider().GetUtcNow().UtcDateTime,
        };
        if (status == ReportStatus.Completed)
        {
            report.ResultJson = ReportResultJson.Serialize(ReportCalculator.Summarize(Array.Empty<ReportExpenseRow>(), report.CreatedAtUtc));
            report.CompletedAtUtc = report.CreatedAtUtc;
        }

        _store.Reports[report.Id] = report;
        return report;
    }

    /// <summary>Authenticates from test headers; without them the request stays anonymous and is challenged with 401.</summary>
    private sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";
        public const string RolesHeader = "X-Test-Roles";

        public HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? user = Request.Headers[UserHeader];
            if (string.IsNullOrWhiteSpace(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            IEnumerable<Claim> claims = Request.Headers[RolesHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role))
                .Prepend(new Claim(ClaimTypes.NameIdentifier, user));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
