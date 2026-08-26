using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Infrastructure.Adapters;

/// <summary>
/// Universal store adapter registry and resolver.
/// Iterates all DI-registered IStoreAdapter instances to match incoming URLs.
/// Eliminates hardcoded store checks across the application.
/// </summary>
public class StoreAdapterRegistry : IStoreAdapterRegistry, IStoreAdapterResolver
{
    private readonly List<IStoreAdapter> _adapters;

    public StoreAdapterRegistry(IEnumerable<IStoreAdapter> adapters)
    {
        _adapters = adapters.ToList();
    }

    public IStoreAdapter? Resolve(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return _adapters.FirstOrDefault(a => a.CanHandle(trimmed));
    }

    public IReadOnlyList<IStoreAdapter> GetAll() => _adapters;

    public IReadOnlyList<StoreInfo> GetSupportedStores()
    {
        return _adapters.Select(a => new StoreInfo(
            Name: a.StoreName,
            AdapterKey: a.AdapterKey,
            Domains: a.SupportedDomains,
            IsEnabled: true
        )).ToList();
    }
}

/// <summary>
/// Backwards-compatible alias for StoreAdapterRegistry.
/// </summary>
public class StoreAdapterResolver : StoreAdapterRegistry
{
    public StoreAdapterResolver(IEnumerable<IStoreAdapter> adapters) : base(adapters) { }
}
