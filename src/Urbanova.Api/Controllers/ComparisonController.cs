using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.Comparison;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 9 scenario comparison. Computed on demand from each scenario's latest
/// succeeded run — never stored. Thin by design: query → <see cref="IComparisonService"/> → DTO.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class ComparisonController(
    IComparisonService comparison,
    ICurrentUserService me) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/comparison")]
    [ProducesResponseType(typeof(ComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ComparisonResponse>> Compare(
        Guid projectId,
        [FromQuery] Guid baselineId,
        [FromQuery] Guid alternativeId,
        CancellationToken ct)
    {
        if (baselineId == Guid.Empty || alternativeId == Guid.Empty)
            return BadRequest(ProblemDetail(
                "INVALID_COMPARISON", "baselineId and alternativeId query parameters are required.", 400));

        try
        {
            return Ok(await comparison.CompareAsync(me.UserId ?? Guid.Empty, projectId, baselineId, alternativeId, ct));
        }
        catch (ComparisonException ex)
        {
            return ex.ErrorCode switch
            {
                "NOT_FOUND" => NotFound(),
                "FORBIDDEN" => Forbid(),
                _ => BadRequest(ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status400BadRequest)),
            };
        }
    }

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
