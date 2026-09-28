using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Analysis;
using Urbanova.Application.Common;
using Urbanova.Application.Scenarios;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 8 scenarios. Thin by design: validation → <see cref="IScenarioService"/> → DTO.
/// Owner-scoped in the service (404 missing / 403 not-owned); locked baselines → 409,
/// invalid shapes → 400. Analyze delegates to the shared pipeline (201 fresh / 200 replay).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class ScenariosController(
    IScenarioService scenarios,
    ICurrentUserService me) : ControllerBase
{
    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost("projects/{projectId:guid}/scenarios")]
    [ProducesResponseType(typeof(ScenarioResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ScenarioResponse>> Create(
        Guid projectId,
        [FromBody] CreateScenarioRequest request,
        [FromServices] IValidator<CreateScenarioRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(ToProblem(validation));

        try
        {
            var created = await scenarios.CreateAsync(OwnerId(), projectId, request, ct);
            return CreatedAtAction(nameof(GetById), new { scenarioId = created.Id }, created);
        }
        catch (ScenarioException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("projects/{projectId:guid}/scenarios")]
    [ProducesResponseType(typeof(ScenarioResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        try
        {
            return Ok(await scenarios.ListAsync(OwnerId(), projectId, ct));
        }
        catch (ScenarioException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("scenarios/{scenarioId:guid}")]
    [ProducesResponseType(typeof(ScenarioResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ScenarioResponse>> GetById(Guid scenarioId, CancellationToken ct)
    {
        try
        {
            return Ok(await scenarios.GetAsync(OwnerId(), scenarioId, ct));
        }
        catch (ScenarioException ex)
        {
            return Map(ex);
        }
    }

    [HttpPut("scenarios/{scenarioId:guid}")]
    [ProducesResponseType(typeof(ScenarioResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ScenarioResponse>> Update(
        Guid scenarioId,
        [FromBody] UpdateScenarioRequest request,
        [FromServices] IValidator<UpdateScenarioRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(ToProblem(validation));

        try
        {
            return Ok(await scenarios.UpdateAsync(OwnerId(), scenarioId, request, ct));
        }
        catch (ScenarioException ex)
        {
            return Map(ex);
        }
    }

    [HttpPost("scenarios/{scenarioId:guid}/analyze")]
    [ProducesResponseType(typeof(AnalysisRunResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AnalysisRunResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnalysisRunResponse>> Analyze(
        Guid scenarioId,
        [FromBody] AnalyzeScenarioRequest request,
        CancellationToken ct)
    {
        try
        {
            var (response, isNew) = await scenarios.AnalyzeAsync(OwnerId(), scenarioId, request, ct);
            return isNew
                ? CreatedAtAction("GetById", "Analysis", new { runId = response.RunId }, response)
                : Ok(response);
        }
        catch (ScenarioException ex)
        {
            return Map(ex);
        }
        catch (AnalysisException ex)
        {
            // Geometry/invalid-file failures from the shared pipeline (422), failures (500).
            return ex.ErrorCode switch
            {
                "INVALID_FILE" or "GEOMETRY_EXTRACTION_FAILED" => StatusCode(
                    StatusCodes.Status422UnprocessableEntity,
                    ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status422UnprocessableEntity)),
                _ => StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status500InternalServerError)),
            };
        }
    }

    private ActionResult Map(ScenarioException ex) => ex.ErrorCode switch
    {
        "NOT_FOUND" => NotFound(),
        "FORBIDDEN" => Forbid(),
        "SCENARIO_LOCKED" or "CONCURRENCY_CONFLICT" => Conflict(
            ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status409Conflict)),
        _ => BadRequest(ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status400BadRequest)),
    };

    private static ValidationProblemDetails ToProblem(FluentValidation.Results.ValidationResult validation) =>
        new(validation.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

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
