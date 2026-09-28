using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.Recommendations;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 10 recommendations. Generated deterministically from an analysis run
/// (explicit runId, else the project's latest succeeded run) and refreshed
/// idempotently. Thin by design: query → <see cref="IRecommendationService"/> → DTOs.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class RecommendationsController(
    IRecommendationService recommendations,
    ICurrentUserService me) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/recommendations")]
    [ProducesResponseType(typeof(RecommendationResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Get(
        Guid projectId,
        [FromQuery] Guid? runId,
        CancellationToken ct)
    {
        try
        {
            var result = await recommendations.GetForProjectAsync(me.UserId ?? Guid.Empty, projectId, runId, ct);
            return Ok(result);
        }
        catch (RecommendationException ex)
        {
            return ex.ErrorCode switch
            {
                "NOT_FOUND" => NotFound(),
                "FORBIDDEN" => Forbid(),
                "NO_EVIDENCE" => StatusCode(
                    StatusCodes.Status422UnprocessableEntity,
                    ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status422UnprocessableEntity)),
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
