namespace Urbanova.Application.Auth;

/// <summary>
/// Auth use cases (implemented in Infrastructure where Identity + EF live).
/// Throws <see cref="AuthException"/> on expected failures.
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
