using Microsoft.Playwright;
using Xunit.Abstractions;

namespace StockTracker.Tests;

public class LiveZaraPlaywrightTests
{
    private readonly ITestOutputHelper _output;

    public LiveZaraPlaywrightTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Manual live Zara test")]
    public async Task TestZaraLiveDetail()
    {
        var url = "https://www.zara.com/tr/tr/100-keten-ince-ceket-p08281012.html";
        using var playwright = await Playwright.CreateAsync();
        
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "chrome",
            Headless = true,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox"
            }
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "tr-TR"
        });

        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        var page = await context.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForTimeoutAsync(2000);

        // Print full JSON-LD
        var jsonLdList = await page.Locator("script[type='application/ld+json']").AllInnerTextsAsync();
        foreach (var j in jsonLdList)
        {
            _output.WriteLine($"=== FULL JSON-LD ===");
            _output.WriteLine(j);
        }

        // Also let's check size selector in DOM:
        // On Zara PDP, sizes might be in a list or inside a size selector button / drawer
        var allButtons = await page.Locator("button, ul li, [data-qa-id]").AllAsync();
        _output.WriteLine($"\n=== Relevant Elements ===");
        foreach (var b in allButtons)
        {
            var txt = (await b.InnerTextAsync()).Trim();
            var dataQaId = await b.GetAttributeAsync("data-qa-id");
            var dataQaAction = await b.GetAttributeAsync("data-qa-action");
            if (dataQaId != null || dataQaAction != null || txt.Contains("EU") || txt.Contains("XS") || txt.Contains("S") || txt.Contains("M") || txt.Contains("L") || txt.Contains("XL"))
            {
                var tag = await b.EvaluateAsync<string>("el => el.tagName");
                _output.WriteLine($"<{tag}> text='{txt.Replace("\n", " ")}' | data-qa-id='{dataQaId}' | data-qa-action='{dataQaAction}'");
            }
        }
    }
}
