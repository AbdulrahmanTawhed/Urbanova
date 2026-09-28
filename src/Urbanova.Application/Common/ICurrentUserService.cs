namespace Urbanova.Application.Common;

/// <summary>Caller identity from the validated JWT (sub claim). Implemented in Api via HttpContext.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    string? Email { get; }
}
