using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbanova.Domain.Costing;

namespace Urbanova.Infrastructure.Costing;

/// <summary>
/// File-backed MVP catalog. Shape: {"currency":"USD","items":[{"code","description",
/// "unit","unitPrice","source"}]}. Read per lookup (tiny file, always fresh);
/// missing/unreadable file → every lookup misses (Unavailable, never invented).
/// </summary>
public sealed class FilePriceCatalog(
    IOptions<PriceCatalogOptions> options,
    ILogger<FilePriceCatalog> logger) : IPriceCatalog
{
    private readonly PriceCatalogOptions _options = options.Value;

    public async Task<PriceLookupResult> TryGetPriceAsync(string itemCode, CancellationToken ct = default)
    {
        var path = Path.IsPathRooted(_options.Path)
            ? _options.Path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.Path));
        if (!File.Exists(path))
        {
            logger.LogDebug("Price catalog file {Path} not found; lookup misses.", path);
            return PriceLookupResult.Miss();
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, useAsync: true);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return PriceLookupResult.Miss();

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("code", out var code)
                    && code.GetString()?.Equals(itemCode, StringComparison.OrdinalIgnoreCase) == true
                    && item.TryGetProperty("unitPrice", out var price)
                    && price.ValueKind == JsonValueKind.Number
                    && item.TryGetProperty("unit", out var unit))
                {
                    var source = item.TryGetProperty("source", out var s) ? s.GetString() : null;
                    return PriceLookupResult.Hit(new CatalogPrice(
                        itemCode, price.GetDecimal(), unit.GetString() ?? string.Empty,
                        source ?? "catalog"));
                }
            }
            return PriceLookupResult.Miss();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Price catalog unreadable; lookup misses.");
            return PriceLookupResult.Miss();
        }
    }
}
