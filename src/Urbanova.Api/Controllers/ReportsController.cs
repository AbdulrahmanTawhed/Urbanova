using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.Reporting;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 12 reporting. Canonical JSON always persisted; HTML additionally filed.
/// Thin by design: request → <see cref="IReportService"/> → DTO / file bytes.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class ReportsController(
    IReportService reports,
    ICurrentUserService me) : ControllerBase
{
    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost("projects/{projectId:guid}/reports")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReportResponse>> Generate(
        Guid projectId,
        [FromBody] CreateReportRequest request,
        CancellationToken ct)
    {
        try
        {
            var created = await reports.GenerateAsync(OwnerId(), projectId, request, ct);
            return CreatedAtAction(nameof(GetById), new { reportId = created.Id }, created);
        }
        catch (ReportException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("reports/{reportId:guid}")]
    [ProducesResponseType(typeof(ReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportResponse>> GetById(Guid reportId, CancellationToken ct)
    {
        try
        {
            return Ok(await reports.GetAsync(OwnerId(), reportId, ct));
        }
        catch (ReportException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("reports/{reportId:guid}/file")]
    [Produces("text/html", "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFile(Guid reportId, CancellationToken ct)
    {
        try
        {
            var (contentType, content, fileName) = await reports.GetFileAsync(OwnerId(), reportId, ct);
            return File(content, contentType, fileName);
        }
        catch (ReportException ex)
        {
            return Map(ex);
        }
    }

    private ActionResult Map(ReportException ex) => ex.ErrorCode switch
    {
        "NOT_FOUND" => NotFound(),
        "FORBIDDEN" => Forbid(),
        _ => BadRequest(ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status400BadRequest)),
    };

    private ProblemDetails ProblemDetail(string code, string detail, int status) => new()
    {
        Type = $"https://urbanova.local/errors/{code.ToLowerInvariant()}",
        Title = code,
        Status = status,
        Detail = detail,
        Instance = Request.Path,
        Extensions = { ["traceId"] = HttpContext.TraceIdentifier, ["code"] = code },
    };
}
