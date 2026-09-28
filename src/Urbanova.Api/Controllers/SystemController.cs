using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbanova.Api.Controllers;

/// <summary>
/// Phase 1 diagnostics controller. No business logic — only build/environment info.
/// Business controllers (Projects, Files, Analysis, ...) arrive from Phase 4 onward
/// and must stay thin: validation -&gt; Application service -&gt; Domain -&gt; persistence.
/// </summary>
[ApiController]
[Route("api/system")]
[Produces("application/json")]
[AllowAnonymous] // public diagnostics; all business endpoints require JWT (fallback policy)
public sealed class SystemController : ControllerBase
{
    /// <summary>Build + stack info for smoke tests and ops.</summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(SystemInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo(IHostEnvironment env)
        => Ok(new SystemInfoResponse(
            Service: "urbanova-backend",
            Phase: "phase-14-complete",
            Dotnet: Environment.Version.ToString(),
            Environment: env.EnvironmentName,
            TimeUtc: DateTimeOffset.UtcNow));

    public sealed record SystemInfoResponse(
        string Service,
        string Phase,
        string Dotnet,
        string Environment,
        DateTimeOffset TimeUtc);
}
