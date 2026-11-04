using System;
using System.Collections.Generic;
using System.Linq;
using ExpenseHub.Core.Models;
using ExpenseHub.Core.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests.Reports;

/// <summary>Checks eligibility and summarization with the professor's verifiable example and edge cases.</summary>
[TestClass]
public sealed class ReportCalculatorTests
{
    private static readonly DateTime _generatedAt = new(2026, 10, 1, 12, 0, 2, DateTimeKind.Utc);
    private static readonly string[] _lifecycle = { "Draft", "Submitted", "Approved", "Rejected", "Paid" };

    /// <summary>Mine: three own expenses inside the period, R$ 175,00, excluding foreign and out-of-period ones.</summary>
    [TestMethod]
    public void MineMatchesTheReferenceExample()
    {
        ReportResult result = Calculate(ReportScope.Mine);
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(175.00m, result.TotalAmount);
        AssertState(result, "Approved", 2, 150.00m);
        AssertState(result, "Paid", 1, 25.00m);
        AssertState(result, "Draft", 0, 0m);
        Assert.AreEqual(_generatedAt, result.GeneratedAt);
    }

    /// <summary>Global: four expenses and R$ 375,00, still excluding the out-of-period one.</summary>
    [TestMethod]
    public void GlobalMatchesTheReferenceExample()
    {
        ReportResult result = Calculate(ReportScope.Global);
        Assert.AreEqual(4, result.TotalCount);
        Assert.AreEqual(375.00m, result.TotalAmount);
        AssertState(result, "Approved", 3, 350.00m);
    }

    /// <summary>Both period boundaries are inclusive and the day outside each boundary is excluded.</summary>
    /// <param name="day">The expense date.</param>
    /// <param name="eligible">Whether it enters the report.</param>
    [TestMethod]
    [DataRow("2026-08-31", false)]
    [DataRow("2026-09-01", true)]
    [DataRow("2026-09-30", true)]
    [DataRow("2026-10-01", false)]
    public void PeriodBoundariesAreInclusive(string day, bool eligible)
    {
        Func<Expense, bool> predicate = ReportCalculator.Eligible(Request(ReportScope.Mine)).Compile();
        Assert.AreEqual(eligible, predicate(ExpenseOf("requester", DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>Mine never counts another owner's expense, even inside the period.</summary>
    [TestMethod]
    public void MineExcludesOtherOwners()
    {
        Func<Expense, bool> predicate = ReportCalculator.Eligible(Request(ReportScope.Mine)).Compile();
        Assert.IsFalse(predicate(ExpenseOf("someone-else", new DateOnly(2026, 9, 15))));
    }

    /// <summary>No eligible expense still yields all five states with zero, in lifecycle order.</summary>
    [TestMethod]
    public void EmptyReportHasFiveZeroStates()
    {
        ReportResult result = ReportCalculator.Summarize(Array.Empty<ReportExpenseRow>(), _generatedAt);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(0m, result.TotalAmount);
        CollectionAssert.AreEqual(_lifecycle, result.ByStatus.Select(total => total.Status).ToArray());
        Assert.IsTrue(result.ByStatus.All(total => total.Count == 0 && total.Amount == 0m));
    }

    /// <summary>Sums are exact decimals: 0.1 + 0.2 is exactly 0.3, unlike binary floating point.</summary>
    [TestMethod]
    public void SumsAreExactDecimals()
    {
        var rows = new List<ReportExpenseRow> { new(ExpenseStatus.Submitted, 0.1m), new(ExpenseStatus.Submitted, 0.2m) };
        ReportResult result = ReportCalculator.Summarize(rows, _generatedAt);
        Assert.AreEqual(0.3m, result.TotalAmount);
        AssertState(result, "Submitted", 2, 0.3m);
    }

    private static ReportResult Calculate(ReportScope scope)
    {
        var expenses = new List<Expense>
        {
            ExpenseOf("requester", new DateOnly(2026, 9, 5), ExpenseStatus.Approved, 100.00m),
            ExpenseOf("requester", new DateOnly(2026, 9, 10), ExpenseStatus.Approved, 50.00m),
            ExpenseOf("requester", new DateOnly(2026, 9, 20), ExpenseStatus.Paid, 25.00m),
            ExpenseOf("other-employee", new DateOnly(2026, 9, 12), ExpenseStatus.Approved, 200.00m),
            ExpenseOf("requester", new DateOnly(2026, 10, 2), ExpenseStatus.Paid, 500.00m),
        };
        Func<Expense, bool> eligible = ReportCalculator.Eligible(Request(scope)).Compile();
        IEnumerable<ReportExpenseRow> rows = expenses.Where(eligible).Select(expense => new ReportExpenseRow(expense.Status, expense.Amount));
        return ReportCalculator.Summarize(rows, _generatedAt);
    }

    private static void AssertState(ReportResult result, string state, int count, decimal amount)
    {
        ReportStatusTotal total = result.ByStatus.Single(item => item.Status == state);
        Assert.AreEqual(count, total.Count, state);
        Assert.AreEqual(amount, total.Amount, state);
    }

    private static ReportRequest Request(ReportScope scope) => new()
    {
        RequesterId = "requester",
        Scope = scope,
        FromDate = new DateOnly(2026, 9, 1),
        ToDate = new DateOnly(2026, 9, 30),
    };

    private static Expense ExpenseOf(string owner, DateOnly date, ExpenseStatus status = ExpenseStatus.Approved, decimal amount = 10m) => new()
    {
        OwnerId = owner,
        ExpenseDate = date,
        Status = status,
        Amount = amount,
    };
}
