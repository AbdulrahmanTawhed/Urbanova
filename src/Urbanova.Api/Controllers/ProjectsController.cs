using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.Projects;
using Urbanova.Infrastructure.Auth.Ownership;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 4 project management. Thin by design: validation → <see cref="IProjectService"/> → DTO.
/// Id-scoped actions first return 404 for missing projects, then enforce the
/// MustOwnProject policy (403) — service ownership checks add defense in depth.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
[Produces("application/json")]
public sealed class ProjectsController(
    IProjectService projects,
    ICurrentUserService me,
    IAuthorizationService authz) : ControllerBase
{
    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectResponse>> Create(
        [FromBody] CreateProjectRequest request,
        [FromServices] IValidator<CreateProjectRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(ToProblem(validation));

        var created = await projects.CreateAsync(OwnerId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        // Only the caller's own projects are ever queried (owner-scoped service).
        return Ok(await projects.ListAsync(OwnerId(), page, pageSize, ct));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id, CancellationToken ct)
    {
        ProjectResponse project;
        try
        {
            // 404 before 403: missing ids must not read as forbidden.
            project = await projects.GetAsync(OwnerId(), id, ct);
        }
        catch (ProjectException ex) when (ex.ErrorCode is "NOT_FOUND" or "FORBIDDEN")
        {
            // Service already owner-scoped; map precisely without leaking.
            return ex.ErrorCode == "NOT_FOUND" ? NotFound() : Forbid();
        }

        // Policy enforcement on the HTTP path (service re-checks inside mutating calls).
        var authorized = await authz.AuthorizeAsync(User, id, MustOwnProjectHandler.PolicyName);
        if (!authorized.Succeeded)
            return Forbid();

        return Ok(project);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectResponse>> Update(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        [FromServices] IValidator<UpdateProjectRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(ToProblem(validation));

        var authorized = await authz.AuthorizeAsync(User, id, MustOwnProjectHandler.PolicyName);
        if (!authorized.Succeeded)
        {
            // Distinguish missing (404) from not-owned (403) without leaking ownership.
            return await ExistsForCaller(id, ct) ? Forbid() : (ActionResult<ProjectResponse>)NotFound();
        }

        try
        {
            return Ok(await projects.UpdateAsync(OwnerId(), id, request, ct));
        }
        catch (ProjectException ex)
        {
            return ex.ErrorCode switch
            {
                "NOT_FOUND" => NotFound(),
                "FORBIDDEN" => Forbid(),
                _ => Conflict(ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status409Conflict)),
            };
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var authorized = await authz.AuthorizeAsync(User, id, MustOwnProjectHandler.PolicyName);
        if (!authorized.Succeeded)
            return await ExistsForCaller(id, ct) ? Forbid() : (IActionResult)NotFound();

        try
        {
            await projects.DeleteAsync(OwnerId(), id, ct);
            return NoContent();
        }
        catch (ProjectException ex)
        {
            return ex.ErrorCode == "NOT_FOUND" ? NotFound() : Forbid();
        }
    }

    /// <summary>Existence probe scoped to the caller (used only to pick 403 vs 404).</summary>
    private async Task<bool> ExistsForCaller(Guid id, CancellationToken ct)
    {
        try
        {
            await projects.GetAsync(OwnerId(), id, ct);
            return true;
        }
        catch (ProjectException ex) when (ex.ErrorCode == "FORBIDDEN")
        {
            return true; // exists, owned by someone else
        }
        catch (ProjectException ex) when (ex.ErrorCode == "NOT_FOUND")
        {
            return false;
        }
    }

    private ValidationProblemDetails ToProblem(FluentValidation.Results.ValidationResult validation) =>
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
