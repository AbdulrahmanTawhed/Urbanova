namespace Urbanova.Infrastructure.Costing;

/// <summary>Binds to "PriceCatalog". MVP = JSON file; missing file means every lookup misses.</summary>
public sealed class PriceCatalogOptions
{
    public const string SectionName = "PriceCatalog";

    public string Path { get; set; } = "AppData/pricing/mvp-prices.json";

    public string Currency { get; set; } = "USD";
}
