using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.Costing;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 11 cost estimation. Thin by design: validation → <see cref="ICostService"/> → DTO.
/// Unknown catalog codes answer 201 with Status Unavailable — never an invented price.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class CostEstimatesController(
    ICostService costs,
    ICurrentUserService me) : ControllerBase
{
    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost("projects/{projectId:guid}/cost-estimates")]
    [ProducesResponseType(typeof(CostEstimateResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CostEstimateResponse>> Create(
        Guid projectId,
        [FromBody] CreateCostEstimateRequest request,
        [FromServices] IValidator<CreateCostEstimateRequest> validator,
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
            var created = await costs.CreateAsync(OwnerId(), projectId, request, ct);
            return CreatedAtAction(nameof(GetById), new { estimateId = created.Id }, created);
        }
        catch (CostException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("projects/{projectId:guid}/cost-estimates")]
    [ProducesResponseType(typeof(CostEstimateResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        try
        {
            return Ok(await costs.ListAsync(OwnerId(), projectId, ct));
        }
        catch (CostException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("cost-estimates/{estimateId:guid}")]
    [ProducesResponseType(typeof(CostEstimateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CostEstimateResponse>> GetById(Guid estimateId, CancellationToken ct)
    {
        try
        {
            return Ok(await costs.GetAsync(OwnerId(), estimateId, ct));
        }
        catch (CostException ex)
        {
            return Map(ex);
        }
    }

    private ActionResult Map(CostException ex) => ex.ErrorCode switch
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
