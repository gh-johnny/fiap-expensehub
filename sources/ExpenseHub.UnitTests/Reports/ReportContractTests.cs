using System;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Covers server-side defaults, envelope mapping edges and the publication failure type.</summary>
[TestClass]
public sealed class ReportContractTests
{
    /// <summary>A new request starts Pending, unprocessed, without result or error, at revision 1.</summary>
    [TestMethod]
    public void NewRequestDefaults()
    {
        var report = new ReportRequest();
        Assert.AreNotEqual(Guid.Empty, report.Id);
        Assert.AreEqual(ReportStatus.Pending, report.Status);
        Assert.AreEqual(0, report.Attempts);
        Assert.AreEqual(1L, report.Revision);
        Assert.IsNull(report.ResultJson);
        Assert.IsNull(report.ErrorCode);
    }

    /// <summary>An error code without message still maps to a non-null sanitized message.</summary>
    [TestMethod]
    public void ErrorWithoutMessageMapsToEmpty()
    {
        ReportResponse view = ReportResultJson.View(new ReportRequest { Status = ReportStatus.Failed, ErrorCode = "report.failed" });
        Assert.AreEqual("report.failed", view.Error!.Code);
        Assert.AreEqual(string.Empty, view.Error.Message);
    }

    /// <summary>The persisted JSON keeps exact decimals and the camelCase contract.</summary>
    [TestMethod]
    public void ResultJsonRoundTripsExactly()
    {
        var result = new ReportResult(new DateTime(2026, 10, 1, 12, 0, 2, DateTimeKind.Utc), 1, 0.30m,
            new[] { new ReportStatusTotal("Paid", 1, 0.30m) });
        string json = ReportResultJson.Serialize(result);
        StringAssert.Contains(json, "\"totalAmount\":0.30", StringComparison.Ordinal);
        StringAssert.Contains(json, "\"byStatus\":[{\"status\":\"Paid\"", StringComparison.Ordinal);
        Assert.AreEqual(0.30m, ReportResultJson.Deserialize(json)!.TotalAmount);
        Assert.IsNull(ReportResultJson.Deserialize(null));
    }

    /// <summary>The publication failure keeps the sanitized message and the broker cause for logs.</summary>
    [TestMethod]
    public void PublishExceptionCarriesSanitizedMessage()
    {
        var cause = new TimeoutException("broker");
        var withCause = new ReportPublishException("Not confirmed.", cause);
        Assert.AreEqual("Not confirmed.", withCause.Message);
        Assert.AreSame(cause, withCause.InnerException);
        Assert.AreEqual("Custom.", new ReportPublishException("Custom.").Message);
        StringAssert.Contains(new ReportPublishException().Message, "not confirmed", StringComparison.Ordinal);
    }
}
