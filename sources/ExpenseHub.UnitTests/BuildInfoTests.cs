using System;
using ExpenseHub.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

/// <summary>Checks how the running build identifies its commit.</summary>
[TestClass]
public sealed class BuildInfoTests
{
    /// <summary>Missing or blank values report "unknown"; stamped values are trimmed.</summary>
    /// <param name="value">The raw environment value.</param>
    /// <param name="expected">The reported commit.</param>
    [TestMethod]
    [DataRow(null, "unknown")]
    [DataRow("", "unknown")]
    [DataRow("   ", "unknown")]
    [DataRow("local", "local")]
    [DataRow(" 3f2a9c1e0b7d4a6f8e5c2b1a0d9e8f7c6b5a4d3e ", "3f2a9c1e0b7d4a6f8e5c2b1a0d9e8f7c6b5a4d3e")]
    public void NormalizesTheStampedCommit(string? value, string expected)
        => Assert.AreEqual(expected, BuildInfo.Normalize(value));

    /// <summary>The process value reflects the environment variable read at startup.</summary>
    [TestMethod]
    public void CommitMatchesTheEnvironment()
        => Assert.AreEqual(BuildInfo.Normalize(Environment.GetEnvironmentVariable(BuildInfo.VersionVariable)), BuildInfo.Commit);
}
