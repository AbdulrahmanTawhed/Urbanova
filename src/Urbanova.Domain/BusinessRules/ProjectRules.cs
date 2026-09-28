namespace Urbanova.Domain.BusinessRules;

/// <summary>Project invariants enforced at Application + Domain layers (never frontend-only).</summary>
public static class ProjectRules
{
    public const int NameMaxLength = 200;

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(name));
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"Project name must be ≤ {NameMaxLength} chars.", nameof(name));
    }
}
