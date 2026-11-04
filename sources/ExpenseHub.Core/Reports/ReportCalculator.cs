using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using ExpenseHub.Core.Models;

namespace ExpenseHub.Core.Reports;

/// <summary>Pure report rules: which expenses enter a report and how they are summarized.</summary>
public static class ReportCalculator
{
    /// <summary>Builds the eligibility predicate from the persisted request, translatable to SQL by EF Core.</summary>
    /// <param name="report">The persisted request; its requester and scope come from the server.</param>
    /// <returns>Expenses whose date is inside the inclusive period and, for Mine, owned by the requester.</returns>
    public static Expression<Func<Expense, bool>> Eligible(ReportRequest report)
    {
        ArgumentNullException.ThrowIfNull(report);
        DateOnly from = report.FromDate;
        DateOnly to = report.ToDate;
        bool global = report.Scope == ReportScope.Global;
        string requesterId = report.RequesterId;
        return expense => expense.ExpenseDate >= from && expense.ExpenseDate <= to
            && (global || expense.OwnerId == requesterId);
    }

    /// <summary>Summarizes rows per state with exact decimal sums; every state appears, even with zero.</summary>
    /// <param name="rows">The eligible rows read during processing.</param>
    /// <param name="generatedAtUtc">The calculation instant in UTC.</param>
    /// <returns>The result to persist.</returns>
    public static ReportResult Summarize(IEnumerable<ReportExpenseRow> rows, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ExpenseStatus[] states = Enum.GetValues<ExpenseStatus>();
        int[] counts = new int[states.Length];
        decimal[] amounts = new decimal[states.Length];
        foreach (ReportExpenseRow row in rows)
        {
            counts[(int)row.Status]++;
            amounts[(int)row.Status] += row.Amount;
        }

        List<ReportStatusTotal> byStatus = states
            .Select(state => new ReportStatusTotal(state.ToString(), counts[(int)state], amounts[(int)state]))
            .ToList();
        return new ReportResult(generatedAtUtc, counts.Sum(), amounts.Sum(), byStatus);
    }
}
