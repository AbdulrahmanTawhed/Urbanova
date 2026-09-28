using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Analysis;
using Urbanova.Application.Common;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 7 environmental analysis. Thin by design: validation → <see cref="IAnalysisService"/> → DTO.
/// Replays (identical inputs + config) return the existing run with 200; fresh runs answer 201.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class AnalysisController(
    IAnalysisService analysis,
    ICurrentUserService me) : ControllerBase
{
    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost("projects/{projectId:guid}/analysis")]
    [ProducesResponseType(typeof(AnalysisRunResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AnalysisRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AnalysisRunResponse>> Run(
        Guid projectId,
        [FromBody] AnalyzeRequest request,
        [FromServices] IValidator<AnalyzeRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));

        try
        {
            var (response, isNew) = await analysis.RunAsync(OwnerId(), projectId, request, ct);
            return isNew
                ? CreatedAtAction(nameof(GetById), new { runId = response.RunId }, response)
                : Ok(response);
        }
        catch (AnalysisException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("analysis/{runId:guid}")]
    [ProducesResponseType(typeof(AnalysisRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnalysisRunResponse>> GetById(Guid runId, CancellationToken ct)
    {
        try
        {
            return Ok(await analysis.GetAsync(OwnerId(), runId, ct));
        }
        catch (AnalysisException ex)
        {
            return Map(ex);
        }
    }

    private ActionResult Map(AnalysisException ex) => ex.ErrorCode switch
    {
        "NOT_FOUND" => NotFound(),
        "FORBIDDEN" => Forbid(),
        "INVALID_FILE" or "GEOMETRY_EXTRACTION_FAILED" => StatusCode(
            StatusCodes.Status422UnprocessableEntity,
            ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status422UnprocessableEntity)),
        _ => StatusCode( // ANALYSIS_FAILED and unexpected: stored as Failed, never partial-valid
            StatusCodes.Status500InternalServerError,
            ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status500InternalServerError)),
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
