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
/// Universal store adapter for Mango (shop.mango.com & mango.com).
/// 
/// Extraction strategies:
/// 1. Schema.org JSON-LD (ProductGroup / Product / offers / hasVariant / @graph)
/// 2. Embedded State (__INITIAL_STATE__ / initialData / __NEXT_DATA__)
/// 3. Intercepted API responses (/services/product/ / /v1/products/)
/// 4. DOM Evaluation of size selector buttons and availability classes
/// </summary>
public class MangoAdapter : IStoreAdapter, IInspectableAdapter
{
    private readonly IBrowserService _browserService;
    private readonly ILogger<MangoAdapter> _logger;

    private static readonly Regex MangoUrlPattern =
        new(@"https?://([a-zA-Z0-9_.-]+\.)?mango\.com(/|$|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string StoreName => "Mango";
    public string AdapterKey => "mango";
    public IReadOnlyList<string> SupportedDomains { get; } = new[] { "shop.mango.com", "mango.com" };

    public MangoAdapter(IBrowserService browserService, ILogger<MangoAdapter> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return MangoUrlPattern.IsMatch(url.Trim());
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

    public async Task<ProductInspectResponse> InspectAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandle(url))
        {
            throw new NotSupportedException($"MangoAdapter bu URL'yi desteklemiyor: {url}");
        }

        _logger.LogInformation("Inspecting Mango product: {Url}", url);

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
                    if ((rUrl.Contains("/services/product/") || rUrl.Contains("/v1/products/") || rUrl.Contains("/catalog/")) &&
                        response.Headers.TryGetValue("content-type", out var ct) && ct.Contains("json"))
                    {
                        var text = await response.TextAsync();
                        if (text.Contains("\"sizes\"") || text.Contains("\"colors\"") || text.Contains("\"garment\""))
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
            {
                throw new InvalidOperationException("Sayfa yüklenemedi — sunucu yanıt vermedi.");
            }

            // Wait a brief moment for dynamic hydration/JSON-LD scripts or size buttons
            try
            {
                await page.WaitForSelectorAsync(
                    "script[type='application/ld+json'], button[data-testid*='size'], .size-selector, .product-actions, h1",
                    new PageWaitForSelectorOptions { Timeout = 8000 });
            }
            catch (TimeoutException) { }

            var html = await page.ContentAsync();

            // ── Strategy 1: JSON-LD Extraction ─────────────────────────────
            var jsonLdResult = TryExtractFromJsonLd(html, url);
            if (jsonLdResult != null && jsonLdResult.Variants.Count > 0)
            {
                _logger.LogInformation("Successfully extracted Mango product via JSON-LD: {Name} ({Count} variants)",
                    jsonLdResult.Name, jsonLdResult.Variants.Count);
                return jsonLdResult;
            }

            // ── Strategy 2: Embedded State ────────────────────────────────
            var stateResult = TryExtractFromEmbeddedState(html, url);
            if (stateResult != null && stateResult.Variants.Count > 0)
            {
                _logger.LogInformation("Successfully extracted Mango product via Embedded State: {Name} ({Count} variants)",
                    stateResult.Name, stateResult.Variants.Count);
                return stateResult;
            }

            // ── Strategy 3: Intercepted API ───────────────────────────────
            if (interceptedJson is not null)
            {
                var apiResult = TryExtractFromInterceptedJson(interceptedJson, url);
                if (apiResult != null && apiResult.Variants.Count > 0)
                {
                    _logger.LogInformation("Successfully extracted Mango product via Intercepted API: {Name} ({Count} variants)",
                        apiResult.Name, apiResult.Variants.Count);
                    return apiResult;
                }
            }

            // ── Strategy 4: DOM Evaluation via Playwright ─────────────────
            var domResult = await TryExtractFromDomAsync(page, url);
            if (domResult != null && domResult.Variants.Count > 0)
            {
                _logger.LogInformation("Successfully extracted Mango product via DOM: {Name} ({Count} variants)",
                    domResult.Name, domResult.Variants.Count);
                return domResult;
            }

            // Fallback product name from title/meta if found
            var title = await page.TitleAsync();
            var cleanName = CleanTitle(title);

            if (jsonLdResult != null && !string.IsNullOrWhiteSpace(jsonLdResult.Name))
            {
                return jsonLdResult;
            }

            return new ProductInspectResponse(
                Store: StoreName,
                Name: cleanName,
                ImageUrl: null,
                Url: url,
                Variants: Array.Empty<VariantAvailabilityDto>()
            );
        }
        finally
        {
            if (page is not null)
            {
                await page.CloseAsync();
            }
        }
    }

    public ProductInspectResponse? TryExtractFromJsonLd(string html, string url)
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
                        if (parsed != null) return EnrichImageFromHtml(parsed, html);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("@graph", out var graphProp) && graphProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in graphProp.EnumerateArray())
                        {
                            var parsed = ParseJsonLdElement(elem, url);
                            if (parsed != null) return EnrichImageFromHtml(parsed, html);
                        }
                    }
                    else
                    {
                        var parsed = ParseJsonLdElement(root, url);
                        if (parsed != null) return EnrichImageFromHtml(parsed, html);
                    }
                }
            }
            catch (JsonException) { }
        }

        return null;
    }

    private static ProductInspectResponse EnrichImageFromHtml(ProductInspectResponse response, string html)
    {
        if (!string.IsNullOrWhiteSpace(response.ImageUrl)) return response;

        var ogMatch = Regex.Match(html, @"<meta[^>]*property=[""']og:image[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!ogMatch.Success)
        {
            ogMatch = Regex.Match(html, @"<meta[^>]*content=[""']([^""']+)[""'][^>]*property=[""']og:image[""']", RegexOptions.IgnoreCase);
        }

        if (ogMatch.Success && !string.IsNullOrWhiteSpace(ogMatch.Groups[1].Value))
        {
            return response with { ImageUrl = ogMatch.Groups[1].Value.Trim() };
        }

        return response;
    }

    private ProductInspectResponse? ParseJsonLdElement(JsonElement root, string url)
    {
        if (!root.TryGetProperty("@type", out var typeProp)) return null;
        var type = typeProp.GetString() ?? "";

        if (!type.Contains("Product", StringComparison.OrdinalIgnoreCase)) return null;

        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Mango Ürün" : "Mango Ürün";

        string? imageUrl = null;
        if (root.TryGetProperty("image", out var imgProp))
        {
            if (imgProp.ValueKind == JsonValueKind.String) imageUrl = imgProp.GetString();
            else if (imgProp.ValueKind == JsonValueKind.Array && imgProp.GetArrayLength() > 0)
            {
                var first = imgProp[0];
                if (first.ValueKind == JsonValueKind.String) imageUrl = first.GetString();
                else if (first.ValueKind == JsonValueKind.Object)
                {
                    if (first.TryGetProperty("contentUrl", out var cProp)) imageUrl = cProp.GetString();
                    else if (first.TryGetProperty("url", out var uProp)) imageUrl = uProp.GetString();
                }
            }
            else if (imgProp.ValueKind == JsonValueKind.Object)
            {
                if (imgProp.TryGetProperty("contentUrl", out var cProp)) imageUrl = cProp.GetString();
                else if (imgProp.TryGetProperty("url", out var uProp)) imageUrl = uProp.GetString();
            }
        }

        var variants = new List<VariantAvailabilityDto>();

        // 1. hasVariant array
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

                variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(vName), isAvailable));
            }
        }
        // 2. offers array or object directly on Product
        else if (root.TryGetProperty("offers", out var offersProp))
        {
            if (offersProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var offer in offersProp.EnumerateArray())
                {
                    var size = offer.TryGetProperty("size", out var sProp) ? sProp.GetString()
                             : offer.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(size))
                    {
                        variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(size), CheckOfferAvailability(offer)));
                    }
                }

                // If offers array existed but items had no explicit size (One Size / Bag / Accessory)
                if (variants.Count == 0 && offersProp.GetArrayLength() > 0)
                {
                    var isAvailable = CheckOfferAvailability(offersProp[0]);
                    variants.Add(new VariantAvailabilityDto("Standart", isAvailable));
                }
            }
            else if (offersProp.ValueKind == JsonValueKind.Object)
            {
                var size = offersProp.TryGetProperty("size", out var sProp) ? sProp.GetString()
                         : offersProp.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Standart";

                var isAvailable = CheckOfferAvailability(offersProp);
                variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(size), isAvailable));
            }
        }

        if (variants.Count > 0)
        {
            return new ProductInspectResponse(StoreName, name, imageUrl, url, variants);
        }

        return null;
    }

    public ProductInspectResponse? TryExtractFromEmbeddedState(string html, string url)
    {
        // Try __INITIAL_STATE__ or __NEXT_DATA__
        var patterns = new[]
        {
            @"window\.__INITIAL_STATE__\s*=\s*(\{.*?\});",
            @"<script id=""__NEXT_DATA__""[^>]*>(.*?)</script>",
            @"window\.initialData\s*=\s*(\{.*?\});"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                var root = doc.RootElement;

                // Traverse props or product
                JsonElement productElem = default;
                if (root.TryGetProperty("props", out var props) &&
                    props.TryGetProperty("pageProps", out var pageProps) &&
                    pageProps.TryGetProperty("product", out var prod))
                {
                    productElem = prod;
                }
                else if (root.TryGetProperty("product", out var p))
                {
                    productElem = p;
                }

                if (productElem.ValueKind == JsonValueKind.Object)
                {
                    var name = productElem.TryGetProperty("name", out var np) ? np.GetString() ?? "Mango Ürün" : "Mango Ürün";
                    var variants = new List<VariantAvailabilityDto>();

                    if (productElem.TryGetProperty("colors", out var colorsElem) && colorsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var color in colorsElem.EnumerateArray())
                        {
                            if (color.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var s in sizes.EnumerateArray())
                                {
                                    var sLabel = s.TryGetProperty("label", out var lp) ? lp.GetString()
                                               : s.TryGetProperty("name", out var snp) ? snp.GetString() : null;
                                    var sAvail = s.TryGetProperty("available", out var ap) ? ap.GetBoolean()
                                               : s.TryGetProperty("stock", out var stp) && stp.GetBoolean();

                                    variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(sLabel), sAvail));
                                }
                            }
                        }
                    }

                    if (variants.Count > 0)
                    {
                        var res = new ProductInspectResponse(StoreName, name, null, url, variants);
                        return EnrichImageFromHtml(res, html);
                    }
                }
            }
            catch { }
        }

        return null;
    }

    public ProductInspectResponse? TryExtractFromInterceptedJson(string json, string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement productElem = root;
            if (root.TryGetProperty("garment", out var garmentElem)) productElem = garmentElem;
            else if (root.TryGetProperty("product", out var pElem)) productElem = pElem;

            var name = productElem.TryGetProperty("name", out var np) ? np.GetString() ?? "Mango Ürün" : "Mango Ürün";

            var variants = new List<VariantAvailabilityDto>();
            if (productElem.TryGetProperty("colors", out var colorsElem) && colorsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var color in colorsElem.EnumerateArray())
                {
                    if (color.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in sizes.EnumerateArray())
                        {
                            var sLabel = s.TryGetProperty("label", out var lp) ? lp.GetString()
                                       : s.TryGetProperty("name", out var snp) ? snp.GetString() : null;
                            var sAvail = s.TryGetProperty("available", out var ap) ? ap.GetBoolean()
                                       : s.TryGetProperty("stock", out var stp) && stp.GetBoolean();

                            variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(sLabel), sAvail));
                        }
                    }
                }
            }

            if (variants.Count > 0)
            {
                return new ProductInspectResponse(StoreName, name, null, url, variants);
            }
        }
        catch { }

        return null;
    }

    private static string NormalizeSizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "Standart";
        var trimmed = label.Trim();
        if (trimmed.Equals("U", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("TU", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("UNICA", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ONE SIZE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("TEK BEDEN", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("TEK EBAT", StringComparison.OrdinalIgnoreCase))
        {
            return "Standart";
        }
        return trimmed;
    }

    private static bool CheckOfferAvailability(JsonElement offerElem)
    {
        if (offerElem.ValueKind == JsonValueKind.Array && offerElem.GetArrayLength() > 0)
        {
            return CheckOfferAvailability(offerElem[0]);
        }

        if (offerElem.TryGetProperty("availability", out var availProp))
        {
            var availStr = availProp.GetString() ?? "";
            return availStr.Contains("InStock", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<ProductInspectResponse?> TryExtractFromDomAsync(IPage page, string url)
    {
        var name = await page.EvaluateAsync<string>(@"() => {
            const h1 = document.querySelector('h1.product-name, h1, .product-detail-name, [data-testid*=""product-name""]');
            return h1 ? h1.textContent.trim() : '';
        }");

        if (string.IsNullOrWhiteSpace(name)) return null;

        var imageUrl = await page.EvaluateAsync<string?>(@"() => {
            const ogImg = document.querySelector('meta[property=""og:image""], meta[name=""twitter:image""]');
            if (ogImg && ogImg.content) return ogImg.content;
            const img = document.querySelector('.product-image img, .product-images img, img.image-zoom, [data-testid*=""product-image""] img, img[src*=""mango""]');
            return img ? (img.src || img.getAttribute('data-src')) : null;
        }");

        var variantsJson = await page.EvaluateAsync<string>(@"() => {
            const results = [];
            const sizeButtons = document.querySelectorAll('button[data-testid*=""size""], button.selector-size, .size-selector button, .product-sizes button, [aria-label*=""Size""], [aria-label*=""Beden""]');

            for (const btn of sizeButtons) {
                const text = btn.innerText || btn.textContent || btn.getAttribute('data-size') || '';
                const cleanText = text.replace(/(\r\n|\n|\r)/gm, ' ').trim();
                if (!cleanText || cleanText.length > 25) continue;

                const isDisabled = btn.disabled || 
                                   btn.classList.contains('disabled') || 
                                   btn.classList.contains('out-of-stock') || 
                                   btn.classList.contains('is-disabled') ||
                                   btn.getAttribute('aria-disabled') === 'true' ||
                                   btn.getAttribute('data-available') === 'false';

                results.push({ name: cleanText, available: !isDisabled });
            }

            // Fallback for one-size products (Bags, Accessories, Jewelry, Perfume)
            if (results.length === 0) {
                const addToBagBtn = document.querySelector('button[data-testid*=""add-to-cart""], button[data-testid*=""add-to-bag""], button[aria-label*=""Sepete""], button.product-actions__button, [data-testid*=""pdp-add-to-bag""]');
                const outOfStockEl = document.querySelector('[data-testid*=""out-of-stock""], .out-of-stock, [aria-label*=""Tükendi""], [aria-label*=""Gelince Haber Ver""]');
                
                if (addToBagBtn || outOfStockEl) {
                    const isAvailable = Boolean(addToBagBtn && !addToBagBtn.disabled && !outOfStockEl);
                    results.push({ name: 'Standart', available: isAvailable });
                }
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
                    variants.Add(new VariantAvailabilityDto(NormalizeSizeLabel(vName), avail));
                }
            }
        }

        return new ProductInspectResponse(StoreName, name, imageUrl, url, variants);
    }

    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Mango Ürün";
        var clean = Regex.Replace(title, @"\s*[-|]\s*MANGO.*$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? title.Trim() : clean;
    }
}
