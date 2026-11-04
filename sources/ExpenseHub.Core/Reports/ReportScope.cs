namespace ExpenseHub.Core.Reports;

/// <summary>Defines which reimbursements a report summarizes.</summary>
public enum ReportScope
{
    /// <summary>Only reimbursements owned by the requester.</summary>
    Mine,
    /// <summary>Every reimbursement in the period, regardless of owner.</summary>
    Global,
}
