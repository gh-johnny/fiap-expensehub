using System;
using System.Text;
using ExpenseHub.Core.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Checks the minimal message contract: only the report identifier travels.</summary>
[TestClass]
public sealed class ReportRequestedMessageTests
{
    /// <summary>The body is exactly {"reportId":"…"} and parses back to the same identifier.</summary>
    [TestMethod]
    public void RoundTripsOnlyTheIdentifier()
    {
        var id = Guid.NewGuid();
        byte[] body = new ReportRequestedMessage(id).ToBytes();
        Assert.AreEqual($"{{\"reportId\":\"{id}\"}}", Encoding.UTF8.GetString(body));
        Assert.IsTrue(ReportRequestedMessage.TryParse(body, out ReportRequestedMessage? parsed));
        Assert.AreEqual(id, parsed!.ReportId);
    }

    /// <summary>Malformed, empty or extra-only bodies are rejected; extra fields such as an owner are ignored.</summary>
    /// <param name="body">The raw body.</param>
    /// <param name="valid">Whether it parses.</param>
    [TestMethod]
    [DataRow("not json", false)]
    [DataRow("null", false)]
    [DataRow("{}", false)]
    [DataRow("{\"reportId\":\"00000000-0000-0000-0000-000000000000\"}", false)]
    [DataRow("{\"reportId\":\"8d1f3c0e-9b7a-4a51-bc0e-3f0d5e2a1b44\",\"ownerId\":\"attacker\"}", true)]
    public void RejectsInvalidBodies(string body, bool valid)
        => Assert.AreEqual(valid, ReportRequestedMessage.TryParse(Encoding.UTF8.GetBytes(body), out _));

    /// <summary>The AMQP message type is stable.</summary>
    [TestMethod]
    public void TypeIsStable() => Assert.AreEqual("expensehub.report.requested", ReportRequestedMessage.MessageType);
}
