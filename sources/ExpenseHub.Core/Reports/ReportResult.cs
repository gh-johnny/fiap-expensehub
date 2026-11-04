using System;
using System.Collections.Generic;
using ExpenseHub.Core.Models;

namespace ExpenseHub.Core.Reports;

/// <summary>The persisted summary of a completed report.</summary>
/// <param name="GeneratedAt">The UTC instant the worker calculated the result.</param>
/// <param name="TotalCount">The number of reimbursements considered.</param>
/// <param name="TotalAmount">The exact sum of their amounts.</param>
/// <param name="ByStatus">One entry per lifecycle state, including zeros, in lifecycle order.</param>
public sealed record ReportResult(DateTime GeneratedAt, int TotalCount, decimal TotalAmount, IReadOnlyList<ReportStatusTotal> ByStatus);

/// <summary>The count and exact amount of reimbursements in one lifecycle state.</summary>
/// <param name="Status">The state name.</param>
/// <param name="Count">The number of reimbursements.</param>
/// <param name="Amount">The exact sum of their amounts.</param>
public sealed record ReportStatusTotal(string Status, int Count, decimal Amount);

/// <summary>The stable code and sanitized message of a definitive failure.</summary>
/// <param name="Code">The machine-readable code.</param>
/// <param name="Message">A message without internal details.</param>
public sealed record ReportError(string Code, string Message);

/// <summary>The only expense data a report needs, read during processing.</summary>
/// <param name="Status">The state at processing time.</param>
/// <param name="Amount">The amount at processing time.</param>
public sealed record ReportExpenseRow(ExpenseStatus Status, decimal Amount);
