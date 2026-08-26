using StockTracker.Application.DTOs;

namespace StockTracker.Application.Interfaces;

/// <summary>
/// Abstraction for fetching a clean product + variant/stock snapshot.
/// Implemented by each store adapter.
/// </summary>
public interface IProductInspector
{
    /// <summary>Returns a clean product inspect result for the given URL.</summary>
    Task<ProductInspectResponse> InspectAsync(string url, CancellationToken cancellationToken = default);
}
