namespace Urbanova.Domain.Costing;

/// <summary>
/// Price catalog port (PRD §14). MVP = file-backed catalog; swap for a real feed later.
/// A miss is a normal outcome (Unavailable) — never an exception, never an invented price.
/// </summary>
public sealed record CatalogPrice(string ItemCode, decimal UnitPrice, string Unit, string Source);

public sealed record PriceLookupResult(bool Found, CatalogPrice? Price)
{
    public static PriceLookupResult Miss() => new(false, null);

    public static PriceLookupResult Hit(CatalogPrice price) => new(true, price);
}

public interface IPriceCatalog
{
    Task<PriceLookupResult> TryGetPriceAsync(string itemCode, CancellationToken ct = default);
}
