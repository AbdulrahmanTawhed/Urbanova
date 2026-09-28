namespace Urbanova.Application.Auth;

/// <summary>
/// Application-level auth failure. Api maps <see cref="ErrorCode"/> to HTTP status;
/// the message is safe to return (no internals).
/// Codes: EMAIL_TAKEN (409), INVALID_CREDENTIALS (401), INVALID_REFRESH_TOKEN (401).
/// </summary>
public sealed class AuthException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
