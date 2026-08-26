namespace StockTracker.Application.DTOs;

/// <summary>
/// Model describing a supported store in the universal adapter engine.
/// </summary>
public record StoreInfo(
    string Name,
    string AdapterKey,
    IReadOnlyList<string> Domains,
    bool IsEnabled = true
);
