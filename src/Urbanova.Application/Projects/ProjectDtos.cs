namespace Urbanova.Application.Projects;

/// <summary>Project DTOs. Controllers map these — EF entities never leave the service layer.</summary>
public sealed record SiteInput(
    string? Address,
    double? Latitude,
    double? Longitude,
    string? BoundaryGeoJson,
    string? Crs,
    double? AreaM2);

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    SiteInput? Site);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string? Status,
    SiteInput? Site,
    byte[]? RowVersion);

public sealed record SiteResponse(
    Guid Id,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? BoundaryGeoJson,
    string Crs,
    double? AreaM2);

public sealed record ProjectResponse(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    byte[]? RowVersion,
    SiteResponse? Site);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
