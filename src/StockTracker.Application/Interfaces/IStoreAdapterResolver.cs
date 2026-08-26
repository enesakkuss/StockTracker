namespace StockTracker.Application.Interfaces;

/// <summary>
/// Resolves the correct IStoreAdapter for a given product URL.
/// </summary>
public interface IStoreAdapterResolver
{
    /// <summary>
    /// Returns the appropriate adapter for the given URL, or null if unsupported.
    /// </summary>
    IStoreAdapter? Resolve(string url);
}
