using System.Reflection;
using FluentAssertions;

namespace Urbanova.UnitTests;

/// <summary>
/// Phase 1 architecture guards: Domain must stay dependency-free (Clean Architecture).
/// Application may use FluentValidation but not EF Core / ASP.NET Core.
/// These guards run on every build to catch layering violations early.
/// </summary>
public sealed class ArchitectureGuardTests
{
    [Fact]
    public void Domain_ShouldNotReferenceInfrastructureConcerns()
    {
        var domain = typeof(Urbanova.Domain.Entities.Project).Assembly;
        var refs = domain.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();

        refs.Should().NotContain("Microsoft.EntityFrameworkCore",
            "Domain must not depend on EF Core (Phase 2+ lives in Infrastructure).");
        refs.Should().NotContain("Microsoft.AspNetCore.Mvc.Core",
            "Domain must not depend on ASP.NET Core.");
        refs.Should().NotContain("Urbanova.Infrastructure",
            "Dependency direction is Infrastructure -> Domain, never the reverse.");
        refs.Should().NotContain("Urbanova.Application",
            "Dependency direction is Application -> Domain, never the reverse.");
    }

    [Fact]
    public void Application_ShouldNotReferenceInfrastructureOrApi()
    {
        var app = typeof(Urbanova.Application.Placeholder).Assembly;
        var refs = app.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();

        refs.Should().NotContain("Microsoft.EntityFrameworkCore");
        refs.Should().NotContain("Urbanova.Infrastructure");
    }
}
