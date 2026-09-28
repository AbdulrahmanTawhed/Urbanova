using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Urbanova.Application.Common;
using Urbanova.Application.EngineeringFiles;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 5 engineering files. Thin by design: input guards → <see cref="IFileService"/> → DTO.
/// Ownership is enforced owner-scoped in the service (404 missing / 403 not-owned); upload
/// rejects unsupported formats (415) and invalid content (422) with explanations,
/// and duplicate content (409). Geometry extraction arrives in Phase 6.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class FilesController(
    IFileService files,
    IGeometryService geometry,
    ICurrentUserService me) : ControllerBase
{
    /// <summary>~5 MB headroom above the 50 MB content limit so the app (not the server) returns 413.</summary>
    private const long RequestSizeLimitBytes = 55_000_000;

    private Guid OwnerId() => me.UserId ?? Guid.Empty;

    [HttpPost("projects/{projectId:guid}/files")]
    [RequestSizeLimit(RequestSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FileResponse>> Upload(
        Guid projectId,
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ProblemDetail("EMPTY_FILE", "A non-empty file is required.", StatusCodes.Status400BadRequest));

        try
        {
            await using var stream = file.OpenReadStream();
            var created = await files.UploadAsync(
                OwnerId(), projectId, file.FileName, file.ContentType ?? "application/octet-stream",
                stream, file.Length, ct);
            return CreatedAtAction(nameof(GetById), new { fileId = created.Id }, created);
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("projects/{projectId:guid}/files")]
    [ProducesResponseType(typeof(FileResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        try
        {
            var result = await files.ListAsync(OwnerId(), projectId, ct);
            return Ok(result.Items);
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    [HttpGet("files/{fileId:guid}")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FileResponse>> GetById(Guid fileId, CancellationToken ct)
    {
        try
        {
            return Ok(await files.GetAsync(OwnerId(), fileId, ct));
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    [HttpPost("files/{fileId:guid}/validate")]
    [ProducesResponseType(typeof(FileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FileResponse>> Validate(Guid fileId, CancellationToken ct)
    {
        try
        {
            return Ok(await files.ValidateAsync(OwnerId(), fileId, ct));
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    [HttpPost("files/{fileId:guid}/extract-geometry")]
    [ProducesResponseType(typeof(GeometryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GeometryResponse>> ExtractGeometry(Guid fileId, CancellationToken ct)
    {
        try
        {
            return Ok(await geometry.ExtractAsync(OwnerId(), fileId, ct));
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    [HttpDelete("files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
    {
        try
        {
            await files.DeleteAsync(OwnerId(), fileId, ct);
            return NoContent();
        }
        catch (FileException ex)
        {
            return Map(ex);
        }
    }

    private ActionResult Map(FileException ex)
    {
        // Statement form (not a switch expression) so the default path can bare-rethrow,
        // preserving the original stack: storage/internal failures bubble to the
        // centralized 500 handler without leaking details.
        return ex.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(),
            "FORBIDDEN" => Forbid(),
            "FILE_DUPLICATE" => Conflict(ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status409Conflict)),
            "FILE_TOO_LARGE" => StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status413PayloadTooLarge)),
            "UNSUPPORTED_FORMAT" => StatusCode(
                StatusCodes.Status415UnsupportedMediaType,
                ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status415UnsupportedMediaType)),
            "INVALID_FILE" or "GEOMETRY_EXTRACTION_FAILED" => StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                ProblemDetail(ex.ErrorCode, ex.Message, StatusCodes.Status422UnprocessableEntity)),
            _ => Rethrow(ex),
        };
    }

    private ActionResult Rethrow(FileException ex)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        throw new System.Diagnostics.UnreachableException("ExceptionDispatchInfo.Throw never returns.");
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
