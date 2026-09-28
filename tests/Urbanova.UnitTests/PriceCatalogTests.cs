using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbanova.Application.Costing;
using Urbanova.Infrastructure.Costing;

namespace Urbanova.UnitTests;

/// <summary>Phase 11: catalog lookups + request validation. Temp files — no DB.</summary>
public sealed class PriceCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"urbanova-cat-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private FilePriceCatalog Catalog(string? content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "prices.json");
        if (content is not null)
            File.WriteAllText(path, content);
        return new FilePriceCatalog(
            Options.Create(new PriceCatalogOptions { Path = path }),
            NullLogger<FilePriceCatalog>.Instance);
    }

    private const string Fixture = """
        {"currency":"USD","items":[
          {"code":"veg-planting-m2","description":"x","unit":"m2","unitPrice":8.50,"source":"fixture"},
          {"code":"SHADE-M2","description":"y","unit":"m2","unitPrice":45.00,"source":"fixture"}
        ]}
        """;

    [Fact]
    public async Task Hit_ReturnsPrice()
    {
        var result = await Catalog(Fixture).TryGetPriceAsync("veg-planting-m2");
        result.Found.Should().BeTrue();
        result.Price!.UnitPrice.Should().Be(8.50m);
        result.Price.Unit.Should().Be("m2");
    }

    [Fact]
    public async Task Lookup_IsCaseInsensitive()
    {
        (await Catalog(Fixture).TryGetPriceAsync("shade-m2")).Found.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownCode_Misses()
    {
        (await Catalog(Fixture).TryGetPriceAsync("nope")).Found.Should().BeFalse();
    }

    [Fact]
    public async Task MissingFile_Misses()
    {
        (await Catalog(null).TryGetPriceAsync("veg-planting-m2")).Found.Should().BeFalse();
    }

    [Fact]
    public async Task MalformedFile_Misses()
    {
        (await Catalog("{not json").TryGetPriceAsync("veg-planting-m2")).Found.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsNegativeQuantity()
    {
        new CreateCostEstimateRequestValidator()
            .Validate(new CreateCostEstimateRequest(-1, "m2", 5, null, null, null, null))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_AcceptsCatalogOnly()
    {
        new CreateCostEstimateRequestValidator()
            .Validate(new CreateCostEstimateRequest(10, "m2", null, "veg-planting-m2", null, null, null))
            .IsValid.Should().BeTrue();
    }
}
