using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Infrastructure.Adapters;

/// <summary>
/// Adapter for Zara product pages.
/// 
/// Extraction strategies:
/// 1. Schema.org JSON-LD structured data (ProductGroup / hasVariant)
/// 2. Intercepted internal API / __NEXT_DATA__ JSON
/// 3. DOM fallback
/// </summary>
public class ZaraAdapter : IStoreAdapter, IInspectableAdapter
{
    private readonly IBrowserService _browserService;
    private readonly ILogger<ZaraAdapter> _logger;

    private static readonly Regex ZaraUrlPattern =
        new(@"https?://([a-zA-Z0-9_.-]+\.)?zara\.com(/|$|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string StoreName => "Zara";
    public string StoreType => StoreName;
    public string AdapterKey => "zara";
    public IReadOnlyList<string> SupportedDomains { get; } = new[] { "zara.com" };

    public ZaraAdapter(IBrowserService browserService, ILogger<ZaraAdapter> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public bool CanHandle(string url) =>
        !string.IsNullOrWhiteSpace(url) && ZaraUrlPattern.IsMatch(url);

    public async Task<Product?> FetchProductAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = await InspectAsync(url, cancellationToken);

        return new Product
        {
            Url = result.Url,
            Name = result.Name,
            ImageUrl = result.ImageUrl,
            StoreType = StoreType,
            CreatedAt = DateTime.UtcNow,
            Variants = result.Variants.Select(v => new ProductVariant
            {
                Size = v.Name,
                IsInStock = v.Available,
                LastCheckedAt = DateTime.UtcNow
            }).ToList()
        };
    }

    public async Task<ProductInspectResponse> InspectAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inspecting Zara product: {Url}", url);

        IPage? page = null;
        try
        {
            page = await _browserService.NewPageAsync();

            // Stealth script to patch webdriver
            await page.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                window.chrome = { runtime: {} };
            ");

            // Intercept background product json responses if any
            string? interceptedJson = null;
            page.Response += async (_, response) =>
            {
                try
                {
                    if (interceptedJson is not null || !response.Ok) return;
                    var rUrl = response.Url;
                    if ((rUrl.Contains("/api/catalog/") || rUrl.Contains("/itxrest/")) &&
                        response.Headers.TryGetValue("content-type", out var ct) && ct.Contains("json"))
                    {
                        var text = await response.TextAsync();
                        if (text.Contains("\"sizes\"") || text.Contains("\"detail\""))
                            interceptedJson = text;
                    }
                }
                catch { }
            };

            var gotoResponse = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 45_000
            });

            if (gotoResponse is null)
                throw new InvalidOperationException("Sayfa yüklenemedi — sunucu yanıt vermedi.");

            // Wait a brief moment for dynamic hydration
            await page.WaitForTimeoutAsync(1500);

            var pageTitle = await page.TitleAsync();
            _logger.LogInformation("Loaded Zara page title: {Title}", pageTitle);

            if (pageTitle.Contains("Access Denied", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Access Denied detected, waiting for NetworkIdle...");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
                pageTitle = await page.TitleAsync();
            }

            if (pageTitle.Contains("Access Denied", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Zara sayfası erişimi engelledi (bot koruması). Lütfen tekrar deneyin.");
            }

            // ── Strategy 1: JSON-LD (Schema.org ProductGroup) ─────────────────
            var jsonLdResult = await TryParseJsonLdAsync(page, url);
            if (jsonLdResult is not null)
            {
                _logger.LogInformation("SUCCESS via JSON-LD: {Name}, variants: {Count}", jsonLdResult.Name, jsonLdResult.Variants.Count);
                return jsonLdResult;
            }

            // ── Strategy 2: Intercepted API JSON ─────────────────────────────
            if (interceptedJson is not null)
            {
                var interceptedResult = TryParseInterceptedJson(interceptedJson, url);
                if (interceptedResult is not null)
                {
                    _logger.LogInformation("SUCCESS via Intercepted JSON: {Name}", interceptedResult.Name);
                    return interceptedResult;
                }
            }

            // ── Strategy 3: __NEXT_DATA__ ────────────────────────────────────
            var nextData = await TryGetNextDataAsync(page);
            if (nextData is not null)
            {
                var nextDataResult = ParseNextData(nextData, url);
                if (nextDataResult is not null && nextDataResult.Name != "Bilinmeyen Ürün")
                {
                    _logger.LogInformation("SUCCESS via __NEXT_DATA__: {Name}", nextDataResult.Name);
                    return nextDataResult;
                }
            }

            // ── Strategy 4: DOM Fallback ─────────────────────────────────────
            _logger.LogWarning("JSON strategies failed, attempting DOM fallback for {Url}", url);
            var domResult = await TryDomFallbackAsync(page, url);
            if (domResult is not null)
            {
                return domResult;
            }

            throw new InvalidOperationException("Ürün bilgileri Zara sayfasından okunamadı. Sayfa yapısı değişmiş olabilir.");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout loading Zara page: {Url}", url);
            throw new TimeoutException("Zara sayfası yüklenirken zaman aşımına uğrandı.", ex);
        }
        finally
        {
            if (page is not null)
            {
                try { await page.Context.CloseAsync(); } catch { }
            }
        }
    }

    // ── JSON-LD Parser (Primary Strategy) ───────────────────────────────────

    private async Task<ProductInspectResponse?> TryParseJsonLdAsync(IPage page, string url)
    {
        try
        {
            var scripts = await page.Locator("script[type='application/ld+json']").AllInnerTextsAsync();
            foreach (var json in scripts)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("@type", out var typeProp)) continue;
                var type = typeProp.GetString();

                if (type is "ProductGroup" or "Product")
                {
                    var name = TryGetString(root, "name") ?? "Bilinmeyen Ürün";

                    // Image extraction
                    string? imageUrl = null;
                    if (root.TryGetProperty("image", out var imgEl))
                    {
                        if (imgEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in imgEl.EnumerateArray())
                            {
                                var u = item.GetString();
                                if (!string.IsNullOrEmpty(u)) { imageUrl = u; break; }
                            }
                        }
                        else if (imgEl.ValueKind == JsonValueKind.String)
                        {
                            imageUrl = imgEl.GetString();
                        }
                    }

                    var variants = new List<VariantAvailabilityDto>();

                    if (root.TryGetProperty("hasVariant", out var hasVariant) && hasVariant.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in hasVariant.EnumerateArray())
                        {
                            var sizeName = TryGetString(v, "size") ?? TryGetString(v, "name") ?? "?";
                            var isAvailable = false;

                            if (v.TryGetProperty("offers", out var offers))
                            {
                                var avail = TryGetString(offers, "availability");
                                if (avail is not null)
                                {
                                    isAvailable = avail.Contains("InStock", StringComparison.OrdinalIgnoreCase);
                                }
                            }

                            variants.Add(new VariantAvailabilityDto(sizeName, isAvailable));
                        }
                    }
                    else if (root.TryGetProperty("offers", out var singleOffer))
                    {
                        var avail = TryGetString(singleOffer, "availability");
                        var isAvailable = avail is not null && avail.Contains("InStock", StringComparison.OrdinalIgnoreCase);
                        variants.Add(new VariantAvailabilityDto("Standart", isAvailable));
                    }

                    if (variants.Count > 0)
                    {
                        return new ProductInspectResponse(StoreType, name, imageUrl, url, variants);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON-LD from Zara page");
        }

        return null;
    }

    // ── Intercepted JSON & __NEXT_DATA__ ───────────────────────────────────

    private ProductInspectResponse? TryParseInterceptedJson(string json, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var productEl = root.TryGetProperty("product", out var p) ? p : root;
            return BuildFromProductElement(productEl, url);
        }
        catch { return null; }
    }

    private static async Task<string?> TryGetNextDataAsync(IPage page)
    {
        try
        {
            var loc = page.Locator("script#__NEXT_DATA__");
            if (await loc.CountAsync() > 0)
                return await loc.First.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5000 });
        }
        catch { }
        return null;
    }

    private ProductInspectResponse? ParseNextData(string json, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("props", out var props) &&
                props.TryGetProperty("pageProps", out var pageProps))
            {
                if (pageProps.TryGetProperty("product", out var p) ||
                    pageProps.TryGetProperty("initialProduct", out p))
                {
                    return BuildFromProductElement(p, url);
                }
            }
        }
        catch { }
        return null;
    }

    private ProductInspectResponse? BuildFromProductElement(JsonElement productEl, string url)
    {
        var name = TryGetString(productEl, "name") ?? TryGetString(productEl, "displayName");
        if (string.IsNullOrEmpty(name)) return null;

        var imageUrl = TryExtractImageUrl(productEl);
        var variants = TryExtractVariants(productEl);

        return new ProductInspectResponse(StoreType, name, imageUrl, url, variants);
    }

    private static List<VariantAvailabilityDto> TryExtractVariants(JsonElement productEl)
    {
        var variants = new List<VariantAvailabilityDto>();

        if (productEl.TryGetProperty("detail", out var detail) &&
            detail.TryGetProperty("colors", out var colors) &&
            colors.ValueKind == JsonValueKind.Array)
        {
            foreach (var color in colors.EnumerateArray())
            {
                if (color.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sizes.EnumerateArray())
                    {
                        var sizeName = TryGetString(s, "name") ?? TryGetString(s, "value") ?? "?";
                        var isAvailable = IsSizeAvailable(s);
                        variants.Add(new VariantAvailabilityDto(sizeName, isAvailable));
                    }
                    if (variants.Count > 0) break;
                }
            }
        }

        return variants;
    }

    private static bool IsSizeAvailable(JsonElement sizeEl)
    {
        if (sizeEl.TryGetProperty("availability", out var a) && a.ValueKind == JsonValueKind.String)
        {
            var val = a.GetString()?.ToUpperInvariant();
            return val is "IN_STOCK" or "AVAILABLE" or "INSTOCK";
        }
        if (sizeEl.TryGetProperty("stock", out var s))
        {
            if (s.ValueKind == JsonValueKind.True) return true;
            if (s.ValueKind == JsonValueKind.False) return false;
            if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var q)) return q > 0;
        }
        return false;
    }

    private static string? TryExtractImageUrl(JsonElement productEl)
    {
        if (productEl.TryGetProperty("detail", out var detail) &&
            detail.TryGetProperty("colors", out var colors) &&
            colors.ValueKind == JsonValueKind.Array)
        {
            foreach (var color in colors.EnumerateArray())
            {
                if (color.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var img in images.EnumerateArray())
                    {
                        var u = TryGetString(img, "url") ?? TryGetString(img, "src") ?? TryGetString(img, "path");
                        if (!string.IsNullOrEmpty(u)) return u.StartsWith("//") ? "https:" + u : u;
                    }
                }
            }
        }
        return null;
    }

    // ── DOM Fallback ────────────────────────────────────────────────────────

    private async Task<ProductInspectResponse?> TryDomFallbackAsync(IPage page, string url)
    {
        try
        {
            var name = "Bilinmeyen Ürün";
            var nameEl = page.Locator("h1.product-detail-info__header-name, h1[class*='product'], h1").First;
            if (await nameEl.CountAsync() > 0)
                name = (await nameEl.InnerTextAsync()).Trim();

            if (name == "Bilinmeyen Ürün" || name.Length < 2) return null;

            string? imageUrl = null;
            var imgEl = page.Locator("img.media-image__image, img[class*='product'], img").First;
            if (await imgEl.CountAsync() > 0)
                imageUrl = await imgEl.GetAttributeAsync("src");

            var variants = new List<VariantAvailabilityDto>();
            var sizeButtons = page.Locator("[data-qa-action='size-in-stock'], [data-qa-action='size-out-of-stock'], button[class*='size'], [class*='size-selector'] li");
            var count = await sizeButtons.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var btn = sizeButtons.Nth(i);
                var sizeText = (await btn.InnerTextAsync()).Trim();
                if (string.IsNullOrWhiteSpace(sizeText) || sizeText.Length > 20) continue;
                var action = await btn.GetAttributeAsync("data-qa-action") ?? "";
                var isOos = action.Contains("out-of-stock") || await btn.IsDisabledAsync();
                variants.Add(new VariantAvailabilityDto(sizeText, !isOos));
            }

            return new ProductInspectResponse(StoreType, name, imageUrl, url, variants);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DOM fallback failed");
            return null;
        }
    }

    private static string? TryGetString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }
}
