using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Infrastructure;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>
/// Proves the report authorization rules: the profile x scope matrix on request, requester-only reads
/// (even for Auditor and Admin) and a server-owned requester and scope all the way to the worker calculation.
/// </summary>
[TestClass]
public sealed class ReportAuthorizationTests
{
    private static readonly string[] _clientFields = { "FromDate", "ToDate", "Scope" };
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();

    /// <summary>
    /// All 32 role combinations against both scopes, from an explicit expected table:
    /// Mine requires Employee and Global requires Auditor; no other role grants either.
    /// </summary>
    /// <param name="roles">A bit set: Employee, Approver, Finance, Auditor, Admin.</param>
    /// <param name="mine">Whether the combination may request Mine.</param>
    /// <param name="global">Whether the combination may request Global.</param>
    [TestMethod]
    [DataRow(0, false, false)]
    [DataRow(1, true, false)]
    [DataRow(2, false, false)]
    [DataRow(3, true, false)]
    [DataRow(4, false, false)]
    [DataRow(5, true, false)]
    [DataRow(6, false, false)]
    [DataRow(7, true, false)]
    [DataRow(8, false, true)]
    [DataRow(9, true, true)]
    [DataRow(10, false, true)]
    [DataRow(11, true, true)]
    [DataRow(12, false, true)]
    [DataRow(13, true, true)]
    [DataRow(14, false, true)]
    [DataRow(15, true, true)]
    [DataRow(16, false, false)]
    [DataRow(17, true, false)]
    [DataRow(18, false, false)]
    [DataRow(19, true, false)]
    [DataRow(20, false, false)]
    [DataRow(21, true, false)]
    [DataRow(22, false, false)]
    [DataRow(23, true, false)]
    [DataRow(24, false, true)]
    [DataRow(25, true, true)]
    [DataRow(26, false, true)]
    [DataRow(27, true, true)]
    [DataRow(28, false, true)]
    [DataRow(29, true, true)]
    [DataRow(30, false, true)]
    [DataRow(31, true, true)]
    public async Task EveryRoleCombinationHasExpectedScopePermissions(int roles, bool mine, bool global)
    {
        CurrentActor actor = new("requester", RolesFrom(roles));
        await AssertScopeAsync(actor, "Mine", mine);
        await AssertScopeAsync(actor, "Global", global);
    }

    /// <summary>The convenience check agrees with the service for every role and scope.</summary>
    /// <param name="role">A single granted role.</param>
    /// <param name="scope">The requested scope.</param>
    /// <param name="expected">Whether it is permitted.</param>
    [TestMethod]
    [DataRow(RoleNames.Employee, ReportScope.Mine, true)]
    [DataRow(RoleNames.Employee, ReportScope.Global, false)]
    [DataRow(RoleNames.Auditor, ReportScope.Mine, false)]
    [DataRow(RoleNames.Auditor, ReportScope.Global, true)]
    [DataRow(RoleNames.Admin, ReportScope.Mine, false)]
    [DataRow(RoleNames.Admin, ReportScope.Global, false)]
    [DataRow(RoleNames.Approver, ReportScope.Global, false)]
    [DataRow(RoleNames.Finance, ReportScope.Global, false)]
    public void CanRequestMatchesTheMatrix(string role, ReportScope scope, bool expected)
        => Assert.AreEqual(expected, ReportAuthorization.CanRequest(new CurrentActor("requester", new List<string> { role }), scope));

    /// <summary>Unknown scope values are never permitted, even for an actor holding every role.</summary>
    [TestMethod]
    public void UnknownScopeIsNeverPermitted()
    {
        var actor = new CurrentActor("requester", RoleNames.All);
        Assert.IsFalse(ReportAuthorization.CanRequest(actor, (ReportScope)99));
        ApiProblemException error = Assert.ThrowsExactly<ApiProblemException>(() => ReportAuthorization.EnsureCanRequest(actor, (ReportScope)99));
        Assert.AreEqual(403, error.StatusCode);
    }

    /// <summary>A null actor is a programming error, not a permission decision.</summary>
    [TestMethod]
    public void NullActorIsRejected()
        => Assert.ThrowsExactly<ArgumentNullException>(() => ReportAuthorization.CanRequest(null!, ReportScope.Mine));

    /// <summary>Input is validated before permission: a malformed request is 400 for any profile, and creates nothing.</summary>
    [TestMethod]
    public async Task InvalidInputIsRejectedBeforeThePermissionCheck()
    {
        var request = new CreateReportRequest { FromDate = new DateOnly(2026, 9, 30), ToDate = new DateOnly(2026, 9, 1), Scope = "Global" };
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(Actor("employee", RoleNames.Employee), request, CancellationToken.None));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual("report.invalid_period", error.Code);
        Assert.IsEmpty(_store.Reports);
        Assert.IsEmpty(_publisher.Published);
    }

    /// <summary>The denial does not reveal which roles exist or which role was missing.</summary>
    [TestMethod]
    public async Task DenialMessageIsSanitized()
    {
        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(Actor("employee", RoleNames.Employee), Request("Global"), CancellationToken.None));
        Assert.DoesNotContain(RoleNames.Auditor, error.Message);
        Assert.DoesNotContain("employee", error.Message);
        Assert.IsEmpty(error.Extensions);
    }

    /// <summary>The requester is always the authenticated actor; the client contract has no field to override it.</summary>
    [TestMethod]
    public async Task RequesterAlwaysComesFromTheAuthenticatedActor()
    {
        ReportResponse response = await Service().RequestAsync(Actor("token-user", RoleNames.Employee), Request("Mine"), CancellationToken.None);
        Assert.AreEqual("token-user", _store.Reports[response.Id].RequesterId);
        string[] clientFields = typeof(CreateReportRequest).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(_clientFields, clientFields);
    }

    /// <summary>Two Employees: neither lists, reads nor downloads the other's report.</summary>
    [TestMethod]
    public async Task EmployeesAreIsolatedFromEachOther()
    {
        CurrentActor first = Actor("employee-a", RoleNames.Employee);
        CurrentActor second = Actor("employee-b", RoleNames.Employee);
        ReportResponse own = await Service().RequestAsync(first, Request("Mine"), CancellationToken.None);
        Complete(own.Id);

        Assert.IsEmpty(await Service().ListAsync(second, CancellationToken.None));
        await AssertNotFoundAsync(second, own.Id);
        Assert.AreEqual(own.Id, (await Service().FindCompletedAsync(first, own.Id, CancellationToken.None)).Id);
    }

    /// <summary>Auditor's global privilege is about the data in its own report, never about other users' requests.</summary>
    [TestMethod]
    public async Task AuditorCannotReadAnotherUsersReport()
    {
        ReportResponse employees = await Service().RequestAsync(Actor("employee", RoleNames.Employee), Request("Mine"), CancellationToken.None);
        Complete(employees.Id);
        CurrentActor auditor = Actor("auditor", RoleNames.Auditor);
        ReportResponse own = await Service().RequestAsync(auditor, Request("Global"), CancellationToken.None);

        IReadOnlyList<ReportResponse> listed = await Service().ListAsync(auditor, CancellationToken.None);
        Assert.AreEqual(own.Id, listed.Single().Id);
        await AssertNotFoundAsync(auditor, employees.Id);
    }

    /// <summary>Admin, Approver and Finance gain no implicit access: empty lists and 404 for any existing report.</summary>
    /// <param name="role">The privileged but report-unrelated role.</param>
    [TestMethod]
    [DataRow(RoleNames.Admin)]
    [DataRow(RoleNames.Approver)]
    [DataRow(RoleNames.Finance)]
    public async Task OtherRolesGainNoImplicitAccess(string role)
    {
        ReportResponse employees = await Service().RequestAsync(Actor("employee", RoleNames.Employee), Request("Mine"), CancellationToken.None);
        Complete(employees.Id);
        CurrentActor privileged = Actor("privileged", role);
        Assert.IsEmpty(await Service().ListAsync(privileged, CancellationToken.None));
        await AssertNotFoundAsync(privileged, employees.Id);
    }

    /// <summary>
    /// From request to worker: the published message carries only the id, and the worker calculates with the
    /// requester and scope persisted by the server. Mine never sums another employee's expenses, even when the
    /// requester also holds Auditor; Global from an Auditor sums everyone in the period.
    /// </summary>
    /// <param name="scope">The requested scope.</param>
    /// <param name="count">The expected number of summed expenses.</param>
    /// <param name="amount">The expected exact total.</param>
    [TestMethod]
    [DataRow("Mine", 2, "30.50")]
    [DataRow("Global", 3, "230.50")]
    public async Task PersistedRequesterAndScopeDriveTheWorker(string scope, int count, string amount)
    {
        _store.Expenses.Add(Expense("employee-auditor", 10.25m));
        _store.Expenses.Add(Expense("employee-auditor", 20.25m));
        _store.Expenses.Add(Expense("someone-else", 200.00m));
        ReportResponse response = await Service().RequestAsync(
            Actor("employee-auditor", RoleNames.Employee, RoleNames.Auditor), Request(scope), CancellationToken.None);
        Guid published = _publisher.Published.Single();
        Assert.AreEqual(response.Id, published);

        ReportProcessingOutcome outcome = await Processor().ProcessAsync(published, CancellationToken.None);

        Assert.AreEqual(ReportProcessingOutcome.Completed, outcome);
        ReportRequest stored = _store.Reports[published];
        Assert.AreEqual("employee-auditor", stored.RequesterId);
        Assert.AreEqual(scope, stored.Scope.ToString());
        ReportResult result = ReportResultJson.Deserialize(stored.ResultJson)!;
        Assert.AreEqual(count, result.TotalCount);
        Assert.AreEqual(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), result.TotalAmount);
    }

    private static List<string> RolesFrom(int mask)
    {
        var names = new List<string> { RoleNames.Employee, RoleNames.Approver, RoleNames.Finance, RoleNames.Auditor, RoleNames.Admin };
        return names.Where((_, index) => (mask & (1 << index)) != 0).ToList();
    }

    private static CurrentActor Actor(string id, params string[] roles) => new(id, roles);

    private static CreateReportRequest Request(string scope) => new()
    {
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        Scope = scope,
    };

    private static Expense Expense(string owner, decimal amount) => new()
    {
        OwnerId = owner,
        ExpenseDate = new DateOnly(2026, 9, 15),
        Status = ExpenseStatus.Approved,
        Amount = amount,
    };

    private ReportService Service() => new(_store, _publisher, new FixedTimeProvider(), new ListLogger<ReportService>());

    private ReportProcessor Processor()
        => new(_store, new FixedTimeProvider(), Options.Create(new ReportProcessingOptions()), new ListLogger<ReportProcessor>());

    private async Task AssertScopeAsync(CurrentActor actor, string scope, bool allowed)
    {
        int reportsBefore = _store.Reports.Count;
        int publishedBefore = _publisher.Published.Count;
        if (allowed)
        {
            ReportResponse response = await Service().RequestAsync(actor, Request(scope), CancellationToken.None);
            Assert.AreEqual(scope, response.Scope);
            Assert.AreEqual(actor.UserId, _store.Reports[response.Id].RequesterId);
            Assert.HasCount(reportsBefore + 1, _store.Reports);
            Assert.AreEqual(response.Id, _publisher.Published[^1]);
            Assert.HasCount(publishedBefore + 1, _publisher.Published);
            return;
        }

        ApiProblemException error = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().RequestAsync(actor, Request(scope), CancellationToken.None));
        Assert.AreEqual(403, error.StatusCode);
        Assert.AreEqual("report.scope_forbidden", error.Code);
        Assert.HasCount(reportsBefore, _store.Reports);
        Assert.HasCount(publishedBefore, _publisher.Published);
    }

    private void Complete(Guid id)
    {
        ReportRequest report = _store.Reports[id];
        report.Status = ReportStatus.Completed;
        report.CompletedAtUtc = report.CreatedAtUtc;
        report.ResultJson = ReportResultJson.Serialize(ReportCalculator.Summarize(Array.Empty<ReportExpenseRow>(), report.CreatedAtUtc));
    }

    private async Task AssertNotFoundAsync(CurrentActor actor, Guid id)
    {
        ApiProblemException detail = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindAsync(actor, id, CancellationToken.None));
        Assert.AreEqual(404, detail.StatusCode);
        Assert.AreEqual("report.not_found", detail.Code);
        ApiProblemException excel = await Assert.ThrowsExactlyAsync<ApiProblemException>(
            () => Service().FindCompletedAsync(actor, id, CancellationToken.None));
        Assert.AreEqual(404, excel.StatusCode);
        Assert.AreEqual("report.not_found", excel.Code);
    }
}
