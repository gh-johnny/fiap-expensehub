using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Controllers;
using ExpenseHub.Api.Reports;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using ExpenseHub.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Api;

/// <summary>Checks the report HTTP contract: 202 with Location, own listing and detail, and the Excel attachment.</summary>
[TestClass]
public sealed class ReportsControllerTests
{
    private readonly ReportStoreFake _store = new();
    private readonly ReportPublisherFake _publisher = new();

    /// <summary>A valid POST answers 202 Accepted with Location pointing to the detail route.</summary>
    [TestMethod]
    public async Task CreateReturnsAcceptedWithLocation()
    {
        IActionResult result = await Controller("requester").Create(Request(), CancellationToken.None);
        var accepted = (AcceptedResult)result;
        var body = (ReportResponse)accepted.Value!;
        Assert.AreEqual($"/api/reports/{body.Id}", accepted.Location);
        Assert.AreEqual("Pending", body.Status);
    }

    /// <summary>List and detail answer 200 with the caller's own envelopes.</summary>
    [TestMethod]
    public async Task ListAndDetailReturnOwnEnvelopes()
    {
        ReportsController controller = Controller("requester");
        var created = (ReportResponse)((AcceptedResult)await controller.Create(Request(), CancellationToken.None)).Value!;
        var list = (IReadOnlyList<ReportResponse>)((OkObjectResult)await controller.List(CancellationToken.None)).Value!;
        Assert.AreEqual(created.Id, list.Single().Id);
        var detail = (ReportResponse)((OkObjectResult)await controller.Find(created.Id, CancellationToken.None)).Value!;
        Assert.AreEqual(created.Id, detail.Id);
    }

    /// <summary>The Excel route returns a .xlsx attachment named report-{id}.xlsx.</summary>
    [TestMethod]
    public async Task ExcelReturnsSpreadsheetAttachment()
    {
        var report = new ReportRequest
        {
            RequesterId = "requester",
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 30),
            Status = ReportStatus.Completed,
            ResultJson = ReportResultJson.Serialize(ReportCalculator.Summarize(Array.Empty<ReportExpenseRow>(), DateTime.UtcNow)),
        };
        _store.Reports[report.Id] = report;
        var file = (FileContentResult)await Controller("requester").Excel(report.Id, CancellationToken.None);
        Assert.AreEqual(ReportWorkbook.ContentType, file.ContentType);
        Assert.AreEqual($"report-{report.Id}.xlsx", file.FileDownloadName);
        Assert.IsNotEmpty(file.FileContents);
    }

    private static CreateReportRequest Request() => new()
    {
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
        Scope = "Mine",
    };

    private ReportsController Controller(string userId)
        => new(new ReportService(_store, _publisher, new FixedTimeProvider(), new ListLogger<ReportService>()))
        {
            ControllerContext = Principals.Context(userId, RoleNames.Employee),
        };
}
