using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Reports;

namespace ExpenseHub.Api.Reports;

/// <summary>Builds a real .xlsx from the persisted result only; it never queries reimbursements again.</summary>
internal static class ReportWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static string FileName(Guid id) => "report-" + id.ToString("D", CultureInfo.InvariantCulture) + ".xlsx";

    public static byte[] Create(ReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ReportResult result = report.Result
            ?? throw new InvalidOperationException("Only a Completed report with a persisted result can be exported.");

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add("Report");
        sheet.Cell(1, 1).Value = "ExpenseHub reimbursement report";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        WriteField(sheet, 2, "Report id", report.Id.ToString("D", CultureInfo.InvariantCulture));
        WriteField(sheet, 3, "Scope", report.Scope);
        WriteDate(sheet, 4, "From", report.FromDate);
        WriteDate(sheet, 5, "To", report.ToDate);
        sheet.Cell(6, 1).Value = "Generated at (UTC)";
        sheet.Cell(6, 2).Value = result.GeneratedAt;
        sheet.Cell(6, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";

        const int header = 8;
        sheet.Cell(header, 1).Value = "Status";
        sheet.Cell(header, 2).Value = "Count";
        sheet.Cell(header, 3).Value = "Amount";
        sheet.Range(header, 1, header, 3).Style.Font.Bold = true;
        int row = header + 1;
        foreach (ReportStatusTotal total in result.ByStatus)
        {
            WriteTotal(sheet, row++, total.Status, total.Count, total.Amount);
        }

        WriteTotal(sheet, row, "Total", result.TotalCount, result.TotalAmount);
        sheet.Range(row, 1, row, 3).Style.Font.Bold = true;
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 38;
        sheet.Column(3).Width = 18;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteField(IXLWorksheet sheet, int row, string label, string value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
    }

    private static void WriteDate(IXLWorksheet sheet, int row, string label, DateOnly value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd";
    }

    private static void WriteTotal(IXLWorksheet sheet, int row, string label, int count, decimal amount)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = count;
        sheet.Cell(row, 3).Value = amount;
        sheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
    }
}
