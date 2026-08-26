namespace StockTracker.Infrastructure.Configuration;

/// <summary>
/// Playwright browser configuration. Bound from appsettings "Browser" section.
/// </summary>
public class BrowserOptions
{
    public const string SectionName = "Browser";

    /// <summary>Browser channel: 'chrome', 'msedge', or null for default bundled chromium.</summary>
    public string? Channel { get; set; } = null;

    /// <summary>Run browser in headless mode (no visible window). Default: true</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Default page operation timeout in milliseconds. Default: 30000 (30s)</summary>
    public float PageTimeoutMs { get; set; } = 30_000;

    /// <summary>Navigation (page.GotoAsync) timeout in milliseconds. Default: 45000 (45s)</summary>
    public float NavigationTimeoutMs { get; set; } = 45_000;

    /// <summary>User-agent string sent with every browser request.</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
}
