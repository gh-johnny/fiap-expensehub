using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Reports;
using ExpenseHub.Core.Contracts.Requests;
using ExpenseHub.Core.Contracts.Responses;
using ExpenseHub.Core.Reports;
using ExpenseHub.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseHub.Api.Controllers;

/// <summary>Requests background reports, tracks their progress and downloads completed results as Excel.</summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _service;

    /// <summary>Initializes the thin HTTP adapter.</summary>
    /// <param name="service">The report service.</param>
    public ReportsController(ReportService service)
    {
        _service = service;
    }

    /// <summary>Registers and queues a report; the calculation happens later in the worker.</summary>
    /// <param name="request">The period and scope.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>202 with the Pending envelope and the detail location.</returns>
    [HttpPost]
    [ProducesResponseType<ReportResponse>(202)]
    public async Task<IActionResult> Create(CreateReportRequest request, CancellationToken cancellationToken)
    {
        ReportResponse response = await _service.RequestAsync(CurrentActor.FromPrincipal(User), request, cancellationToken);
        return Accepted(new Uri($"/api/reports/{response.Id}", UriKind.Relative), response);
    }

    /// <summary>Lists only the reports the authenticated user requested.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The own envelopes, newest first.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReportResponse>>(200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _service.ListAsync(CurrentActor.FromPrincipal(User), cancellationToken));

    /// <summary>Reads progress and the persisted result of an own report.</summary>
    /// <param name="id">The report identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The envelope.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReportResponse>(200)]
    public async Task<IActionResult> Find(Guid id, CancellationToken cancellationToken)
        => Ok(await _service.FindAsync(CurrentActor.FromPrincipal(User), id, cancellationToken));

    /// <summary>Downloads the persisted result of an own Completed report as a real .xlsx workbook.</summary>
    /// <param name="id">The report identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>The workbook as an attachment.</returns>
    [HttpGet("{id:guid}/excel")]
    [ProducesResponseType<FileContentResult>(200, ReportWorkbook.ContentType)]
    public async Task<IActionResult> Excel(Guid id, CancellationToken cancellationToken)
    {
        ReportResponse report = await _service.FindCompletedAsync(CurrentActor.FromPrincipal(User), id, cancellationToken);
        return File(ReportWorkbook.Create(report), ReportWorkbook.ContentType, ReportWorkbook.FileName(report.Id));
    }
}
