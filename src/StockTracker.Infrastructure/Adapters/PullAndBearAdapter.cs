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
/// Universal store adapter for Pull&Bear (pullandbear.com).
/// Part of the Inditex group.
/// </summary>
public class PullAndBearAdapter : IStoreAdapter, IInspectableAdapter
{
    private readonly IBrowserService _browserService;
    private readonly ILogger<PullAndBearAdapter> _logger;

    private static readonly Regex PullAndBearUrlPattern =
        new(@"https?://([a-zA-Z0-9_.-]+\.)?pullandbear\.com(/|$|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string StoreName => "Pull&Bear";
    public string AdapterKey => "pullandbear";
    public IReadOnlyList<string> SupportedDomains { get; } = new[] { "pullandbear.com" };

    public PullAndBearAdapter(IBrowserService browserService, ILogger<PullAndBearAdapter> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return PullAndBearUrlPattern.IsMatch(url.Trim());
    }

    public async Task<Product?> FetchProductAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = await InspectAsync(url, cancellationToken);

        return new Product
        {
            Url = result.Url,
            Name = result.Name,
            ImageUrl = result.ImageUrl,
            StoreType = StoreName,
            CreatedAt = DateTime.UtcNow,
            Variants = result.Variants.Select(v => new ProductVariant
            {
                Size = v.Name,
                IsInStock = v.Available,
                LastCheckedAt = DateTime.UtcNow
            }).ToList()
        };
    }

    public async Task<ProductInspectResponse> InspectAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(url))
        {
            throw new NotSupportedException($"PullAndBearAdapter bu URL'yi desteklemiyor: {url}");
        }

        _logger.LogInformation("Inspecting Pull&Bear product: {Url}", url);

        IPage? page = null;
        try
        {
            page = await _browserService.NewPageAsync();

            await page.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                window.chrome = { runtime: {} };
            ");

            string? interceptedJson = null;
            page.Response += async (_, response) =>
            {
                try
                {
                    if (interceptedJson is not null || !response.Ok) return;
                    var rUrl = response.Url;
                    if ((rUrl.Contains("/itxrest/") || rUrl.Contains("/api/catalog/") || rUrl.Contains("productsArray") || rUrl.Contains("/detail")) &&
                        response.Headers.TryGetValue("content-type", out var ct) && ct.Contains("json"))
                    {
                        var text = await response.TextAsync();
                        if (text.Contains("\"sizes\"") || text.Contains("\"detail\"") || text.Contains("\"isBuyable\""))
                            interceptedJson = text;
                    }
                }
                catch { }
            };

            var gotoResponse = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 40_000
            });

            if (gotoResponse is null)
                throw new InvalidOperationException("Sayfa yüklenemedi — sunucu yanıt vermedi.");

            try
            {
                await page.WaitForSelectorAsync(
                    "script[type='application/ld+json'], button[data-testid*='size'], button.size-item, .size-selector, .product-size, h1",
                    new PageWaitForSelectorOptions { Timeout = 8000 });
            }
            catch (TimeoutException) { }

            var html = await page.ContentAsync();

            // Strategy 1: JSON-LD
            var jsonLdResult = TryParseJsonLd(html, url);
            if (jsonLdResult is not null && jsonLdResult.Variants.Count > 0)
            {
                _logger.LogInformation("Pull&Bear: Parsed via JSON-LD: {Name} ({Count} variants)", jsonLdResult.Name, jsonLdResult.Variants.Count);
                return jsonLdResult;
            }

            // Strategy 2: Intercepted Inditex API
            if (interceptedJson is not null)
            {
                var apiResult = TryParseInterceptedJson(interceptedJson, url);
                if (apiResult is not null && apiResult.Variants.Count > 0)
                {
                    _logger.LogInformation("Pull&Bear: Parsed via Intercepted API: {Name} ({Count} variants)", apiResult.Name, apiResult.Variants.Count);
                    return apiResult;
                }
            }

            // Strategy 3: Embedded State
            var stateResult = TryParseEmbeddedState(html, url);
            if (stateResult is not null && stateResult.Variants.Count > 0)
            {
                _logger.LogInformation("Pull&Bear: Parsed via Embedded State: {Name} ({Count} variants)", stateResult.Name, stateResult.Variants.Count);
                return stateResult;
            }

            // Strategy 4: DOM
            var domResult = await TryParseDomAsync(page, url);
            if (domResult is not null && domResult.Variants.Count > 0)
            {
                _logger.LogInformation("Pull&Bear: Parsed via DOM: {Name} ({Count} variants)", domResult.Name, domResult.Variants.Count);
                return domResult;
            }

            if (jsonLdResult is not null && !string.IsNullOrWhiteSpace(jsonLdResult.Name))
            {
                return jsonLdResult;
            }

            var pageTitle = await page.TitleAsync();
            var cleanName = CleanTitle(pageTitle);
            return new ProductInspectResponse(StoreName, cleanName, null, url, Array.Empty<VariantAvailabilityDto>());
        }
        finally
        {
            if (page is not null)
            {
                await page.CloseAsync();
            }
        }
    }

    public ProductInspectResponse? TryParseJsonLd(string html, string url)
    {
        var matches = Regex.Matches(
            html,
            @"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var jsonText = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(jsonText)) continue;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in root.EnumerateArray())
                    {
                        var parsed = ParseJsonLdElement(elem, url);
                        if (parsed is not null) return parsed;
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("@graph", out var graphProp) && graphProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in graphProp.EnumerateArray())
                        {
                            var parsed = ParseJsonLdElement(elem, url);
                            if (parsed is not null) return parsed;
                        }
                    }
                    else
                    {
                        var parsed = ParseJsonLdElement(root, url);
                        if (parsed is not null) return parsed;
                    }
                }
            }
            catch (JsonException) { }
        }

        return null;
    }

    private ProductInspectResponse? ParseJsonLdElement(JsonElement root, string url)
    {
        if (!root.TryGetProperty("@type", out var typeProp)) return null;
        var type = typeProp.GetString() ?? "";

        if (!type.Contains("Product", StringComparison.OrdinalIgnoreCase)) return null;

        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Pull&Bear Ürün" : "Pull&Bear Ürün";

        string? imageUrl = null;
        if (root.TryGetProperty("image", out var imgProp))
        {
            if (imgProp.ValueKind == JsonValueKind.String) imageUrl = imgProp.GetString();
            else if (imgProp.ValueKind == JsonValueKind.Array && imgProp.GetArrayLength() > 0)
                imageUrl = imgProp[0].GetString();
            else if (imgProp.ValueKind == JsonValueKind.Object && imgProp.TryGetProperty("url", out var uProp))
                imageUrl = uProp.GetString();
        }

        var variants = new List<VariantAvailabilityDto>();

        if (root.TryGetProperty("hasVariant", out var hasVariantProp) && hasVariantProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var variantElem in hasVariantProp.EnumerateArray())
            {
                var vName = variantElem.TryGetProperty("size", out var sProp) ? sProp.GetString()
                          : variantElem.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(vName)) continue;

                var isAvailable = false;
                if (variantElem.TryGetProperty("offers", out var offersProp))
                {
                    isAvailable = CheckOfferAvailability(offersProp);
                }

                variants.Add(new VariantAvailabilityDto(vName.Trim(), isAvailable));
            }
        }
        else if (root.TryGetProperty("offers", out var offersProp) && offersProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in offersProp.EnumerateArray())
            {
                var size = offer.TryGetProperty("size", out var sProp) ? sProp.GetString()
                         : offer.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;

                if (!string.IsNullOrWhiteSpace(size))
                {
                    variants.Add(new VariantAvailabilityDto(size.Trim(), CheckOfferAvailability(offer)));
                }
            }
        }

        if (variants.Count > 0)
        {
            return new ProductInspectResponse(StoreName, name, imageUrl, url, variants);
        }

        return null;
    }

    public ProductInspectResponse? TryParseInterceptedJson(string json, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement productElem = root;
            if (root.TryGetProperty("detail", out var detailElem))
                productElem = detailElem;
            else if (root.TryGetProperty("products", out var prodsElem) && prodsElem.ValueKind == JsonValueKind.Array && prodsElem.GetArrayLength() > 0)
                productElem = prodsElem[0];

            var name = productElem.TryGetProperty("name", out var np) ? np.GetString() ?? "Pull&Bear Ürün" : "Pull&Bear Ürün";

            string? imageUrl = null;
            if (productElem.TryGetProperty("colors", out var colorsElem) && colorsElem.ValueKind == JsonValueKind.Array && colorsElem.GetArrayLength() > 0)
            {
                var firstColor = colorsElem[0];
                if (firstColor.TryGetProperty("image", out var imgObj) && imgObj.TryGetProperty("url", out var urlProp))
                    imageUrl = urlProp.GetString();
            }

            var variants = new List<VariantAvailabilityDto>();

            // 1. Direct sizes array
            if (productElem.TryGetProperty("sizes", out var sizesElem) && sizesElem.ValueKind == JsonValueKind.Array)
            {
                ExtractSizesFromArray(sizesElem, variants);
            }
            // 2. Sizes inside colors array
            else if (productElem.TryGetProperty("colors", out var cElem) && cElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var col in cElem.EnumerateArray())
                {
                    if (col.TryGetProperty("sizes", out var cSizes) && cSizes.ValueKind == JsonValueKind.Array)
                    {
                        ExtractSizesFromArray(cSizes, variants);
                        if (variants.Count > 0) break;
                    }
                }
            }

            if (variants.Count > 0)
            {
                return new ProductInspectResponse(StoreName, name, imageUrl, url, variants);
            }
        }
        catch { }

        return null;
    }

    private static void ExtractSizesFromArray(JsonElement sizesElem, List<VariantAvailabilityDto> variants)
    {
        foreach (var sizeObj in sizesElem.EnumerateArray())
        {
            var sName = sizeObj.TryGetProperty("name", out var snp) ? snp.GetString()
                      : sizeObj.TryGetProperty("description", out var sdp) ? sdp.GetString() : null;

            var sVis = sizeObj.TryGetProperty("visibilityValue", out var vvp) ? vvp.GetString() : "";
            var sStock = sizeObj.TryGetProperty("isBuyable", out var ibp) ? ibp.GetBoolean()
                       : sizeObj.TryGetProperty("inStock", out var isp) && isp.GetBoolean();

            if (!string.IsNullOrWhiteSpace(sName))
            {
                var avail = sStock || string.Equals(sVis, "SHOW", StringComparison.OrdinalIgnoreCase);
                variants.Add(new VariantAvailabilityDto(sName.Trim(), avail));
            }
        }
    }

    public ProductInspectResponse? TryParseEmbeddedState(string html, string url)
    {
        var patterns = new[]
        {
            @"window\.__INITIAL_STATE__\s*=\s*(\{.*?\});",
            @"window\.itxState\s*=\s*(\{.*?\});",
            @"<script id=""__NEXT_DATA__""[^>]*>(.*?)</script>"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            var parsed = TryParseInterceptedJson(match.Groups[1].Value, url);
            if (parsed is not null && parsed.Variants.Count > 0) return parsed;
        }

        return null;
    }

    private static bool CheckOfferAvailability(JsonElement offerElem)
    {
        if (offerElem.ValueKind == JsonValueKind.Array && offerElem.GetArrayLength() > 0)
            return CheckOfferAvailability(offerElem[0]);

        if (offerElem.TryGetProperty("availability", out var availProp))
        {
            var availStr = availProp.GetString() ?? "";
            return availStr.Contains("InStock", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<ProductInspectResponse?> TryParseDomAsync(IPage page, string url)
    {
        var name = await page.EvaluateAsync<string>(@"() => {
            const h1 = document.querySelector('h1.product-name, h1.product-title, h1, [data-testid=""product-name""]');
            return h1 ? h1.textContent.trim() : '';
        }");

        if (string.IsNullOrWhiteSpace(name)) return null;

        var imageUrl = await page.EvaluateAsync<string?>(@"() => {
            const img = document.querySelector('.product-media img, .media-image img, img.product-image, [data-testid=""main-image""] img, .product-images img');
            return img ? (img.src || img.getAttribute('data-src')) : null;
        }");

        var variantsJson = await page.EvaluateAsync<string>(@"() => {
            const results = [];
            const sizeButtons = document.querySelectorAll(
                'button[data-testid*=""size""], button.size-item, button.size-list-item, .size-selector button, .sizes-list button, .product-size button, [aria-label*=""Size""], [aria-label*=""Beden""], [data-qa-anchor*=""size""]'
            );

            for (const btn of sizeButtons) {
                const text = btn.innerText || btn.textContent || btn.getAttribute('data-size') || '';
                const cleanText = text.replace(/(\r\n|\n|\r)/gm, ' ').trim();
                if (!cleanText || cleanText.length > 25) continue;

                const isDisabled = btn.disabled || 
                                   btn.classList.contains('disabled') || 
                                   btn.classList.contains('out-of-stock') || 
                                   btn.classList.contains('is-disabled') ||
                                   btn.classList.contains('is-out-of-stock') ||
                                   btn.classList.contains('unavailable') ||
                                   btn.getAttribute('aria-disabled') === 'true' ||
                                   btn.getAttribute('data-available') === 'false' ||
                                   btn.getAttribute('data-stock') === '0';

                results.push({ name: cleanText, available: !isDisabled });
            }

            return JSON.stringify(results);
        }");

        var variants = new List<VariantAvailabilityDto>();
        if (!string.IsNullOrWhiteSpace(variantsJson))
        {
            using var doc = JsonDocument.Parse(variantsJson);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var vName = item.GetProperty("name").GetString();
                var avail = item.GetProperty("available").GetBoolean();
                if (!string.IsNullOrWhiteSpace(vName))
                {
                    variants.Add(new VariantAvailabilityDto(vName.Trim(), avail));
                }
            }
        }

        return new ProductInspectResponse(StoreName, name, imageUrl, url, variants);
    }

    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Pull&Bear Ürün";
        var clean = Regex.Replace(title, @"\s*[-|]\s*Pull&Bear.*$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? title.Trim() : clean;
    }
}
