using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

/// <summary>
/// Universal abstraction for store-specific product adapters (Zara, Mango, Pull&Bear, etc.).
/// </summary>
public interface IStoreAdapter
{
    /// <summary>
    /// Display name of the store (e.g. "Zara", "Mango").
    /// </summary>
    string StoreName { get; }

    /// <summary>
    /// Unique identifier key for this adapter (e.g. "zara", "mango").
    /// </summary>
    string AdapterKey { get; }

    /// <summary>
    /// Domain names supported by this adapter (e.g. ["zara.com"], ["shop.mango.com", "mango.com"]).
    /// </summary>
    IReadOnlyList<string> SupportedDomains { get; }

    /// <summary>
    /// Backwards-compatible alias for StoreName.
    /// </summary>
    string StoreType => StoreName;

    /// <summary>
    /// Determines whether this adapter can handle the given URL.
    /// </summary>
    bool CanHandle(string url);

    /// <summary>
    /// Inspects product information, images, variants and stock availability.
    /// </summary>
    Task<ProductInspectResponse> InspectAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches product entity for internal domain tracking.
    /// </summary>
    Task<Product?> FetchProductAsync(string url, CancellationToken cancellationToken = default);
}
