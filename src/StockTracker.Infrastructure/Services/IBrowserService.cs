using Microsoft.Playwright;

namespace StockTracker.Infrastructure.Services;

/// <summary>
/// Singleton Playwright browser manager.
/// Creates the browser once on first use and reuses it across requests.
/// </summary>
public interface IBrowserService : IAsyncDisposable
{
    /// <summary>
    /// Returns a new browser page (tab) from the shared browser instance.
    /// The caller MUST call page.CloseAsync() when done.
    /// </summary>
    Task<IPage> NewPageAsync();
}
