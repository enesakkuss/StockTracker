using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using StockTracker.Infrastructure.Configuration;

namespace StockTracker.Infrastructure.Services;

/// <summary>
/// Manages a single headless Chromium / Chrome browser instance for the application lifetime.
/// Thread-safe lazy initialization — browser is created only on first page request.
/// Applies stealth settings to minimize bot detection.
/// </summary>
public sealed class PlaywrightBrowserService : IBrowserService
{
    private readonly BrowserOptions _options;
    private readonly ILogger<PlaywrightBrowserService> _logger;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public PlaywrightBrowserService(
        IOptions<BrowserOptions> options,
        ILogger<PlaywrightBrowserService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IPage> NewPageAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var browser = await EnsureBrowserAsync();

        // Create a fresh browser context per request for isolation + stealth
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = _options.UserAgent,
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7",
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
                ["Sec-Ch-Ua"] = "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
                ["Sec-Ch-Ua-Mobile"] = "?0",
                ["Sec-Ch-Ua-Platform"] = "\"Windows\"",
                ["Sec-Fetch-Dest"] = "document",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-Site"] = "none",
                ["Sec-Fetch-User"] = "?1",
                ["Upgrade-Insecure-Requests"] = "1"
            }
        });

        context.SetDefaultTimeout(_options.PageTimeoutMs);
        context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);

        return await context.NewPageAsync();
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true })
            return _browser;

        await _initLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            _playwright = await Playwright.CreateAsync();

            var launchArgs = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--disable-infobars"
            };

            // Channels to try: bundled chromium (null) first, followed by explicit channel if configured, then system channels
            var channelsToTry = new List<string?> { null };
            if (!string.IsNullOrWhiteSpace(_options.Channel) && !string.Equals(_options.Channel, "bundled-chromium", StringComparison.OrdinalIgnoreCase))
            {
                channelsToTry.Insert(0, _options.Channel);
            }
            channelsToTry.Add("chrome");
            channelsToTry.Add("msedge");

            var tried = new HashSet<string>();

            foreach (var channel in channelsToTry)
            {
                var key = channel ?? "bundled-chromium";
                if (!tried.Add(key)) continue;

                try
                {
                    _logger.LogInformation("Launching browser with channel: {Channel}...", key);
                    _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Channel = channel,
                        Headless = _options.Headless,
                        Args = launchArgs
                    });

                    _logger.LogInformation("Browser ({Channel}) launched successfully.", key);
                    return _browser;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to launch browser with channel: {Channel}", key);

                    if (channel is null && (ex.Message.Contains("Executable doesn't exist") || ex.Message.Contains("run the following command") || ex.Message.Contains("playwright.ps1 install")))
                    {
                        _logger.LogInformation("Attempting dynamic Playwright browser installation...");
                        try
                        {
                            Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppContext.BaseDirectory);
                            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                            {
                                Channel = null,
                                Headless = _options.Headless,
                                Args = launchArgs
                            });
                            _logger.LogInformation("Browser (bundled-chromium after dynamic install) launched successfully.");
                            return _browser;
                        }
                        catch (Exception installEx)
                        {
                            _logger.LogError(installEx, "Dynamic Playwright browser installation failed.");
                        }
                    }
                }
            }

            throw new InvalidOperationException("Hiçbir tarayıcı kanalı başlatılamadı.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        _initLock.Dispose();
    }
}
