namespace Urbanova.Domain.ValueObjects;

/// <summary>Processor validation outcome. Errors are safe user-facing messages (codes + detail).</summary>
public sealed record FileValidationResult(
    bool IsValid,
    string FormatDetected,
    IReadOnlyList<string> Errors)
{
    public static FileValidationResult Valid(string format) => new(true, format, []);

    public static FileValidationResult Invalid(string format, params string[] errors) =>
        new(false, format, errors);
}
