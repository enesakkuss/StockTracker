using StockTracker.Application.DTOs;

namespace StockTracker.Application.Interfaces;

/// <summary>
/// Registry managing all available store adapters.
/// Resolves URLs to adapters and provides metadata about supported stores.
/// </summary>
public interface IStoreAdapterRegistry : IStoreAdapterResolver
{
    /// <summary>
    /// Gets all registered store adapters.
    /// </summary>
    IReadOnlyList<IStoreAdapter> GetAll();

    /// <summary>
    /// Gets metadata for all supported stores.
    /// </summary>
    IReadOnlyList<StoreInfo> GetSupportedStores();
}
