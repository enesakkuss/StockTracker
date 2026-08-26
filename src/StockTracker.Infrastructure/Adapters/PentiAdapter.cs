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
/// Universal store adapter for Penti (penti.com).
/// </summary>
public class PentiAdapter : IStoreAdapter, IInspectableAdapter
{
    private readonly IBrowserService _browserService;
    private readonly ILogger<PentiAdapter> _logger;

    private static readonly Regex PentiUrlPattern =
        new(@"https?://([a-zA-Z0-9_.-]+\.)?penti\.com(/|$|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string StoreName => "Penti";
    public string AdapterKey => "penti";
    public IReadOnlyList<string> SupportedDomains { get; } = new[] { "penti.com" };

    public PentiAdapter(IBrowserService browserService, ILogger<PentiAdapter> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return PentiUrlPattern.IsMatch(url.Trim());
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
            throw new NotSupportedException($"PentiAdapter bu URL'yi desteklemiyor: {url}");
        }

        _logger.LogInformation("Inspecting Penti product: {Url}", url);

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
                    if ((rUrl.Contains("/api/product") || rUrl.Contains("/products/") || rUrl.Contains("/occ/v2/")) &&
                        response.Headers.TryGetValue("content-type", out var ct) && ct.Contains("json"))
                    {
                        var text = await response.TextAsync();
                        if (text.Contains("\"variantOptions\"") || text.Contains("\"sizes\"") || text.Contains("\"stock\""))
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

            if (gotoResponse.Status is 403 or 429)
                throw new InvalidOperationException($"Penti sayfası erişimi reddetti (HTTP {gotoResponse.Status} - Bot Koruması/Rate Limit).");

            var pageTitle = await page.TitleAsync();
            if (pageTitle.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                pageTitle.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ||
                pageTitle.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                pageTitle.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Penti sayfası bot koruması nedeniyle erişimi engelledi.");
            }

            try
            {
                await page.WaitForSelectorAsync(
                    "script[type='application/ld+json'], button.size-box, .size-options, .product-size, h1",
                    new PageWaitForSelectorOptions { Timeout = 8000 });
            }
            catch (TimeoutException) { }

            var html = await page.ContentAsync();

            // Strategy 1: JSON-LD
            var jsonLdResult = TryParseJsonLd(html, url);
            if (jsonLdResult is not null && jsonLdResult.Variants.Count > 0)
            {
                _logger.LogInformation("Penti: Parsed via JSON-LD: {Name} ({Count} variants)", jsonLdResult.Name, jsonLdResult.Variants.Count);
                return jsonLdResult;
            }

            // Strategy 2: Intercepted API
            if (interceptedJson is not null)
            {
                var apiResult = TryParseInterceptedJson(interceptedJson, url);
                if (apiResult is not null && apiResult.Variants.Count > 0)
                {
                    _logger.LogInformation("Penti: Parsed via Intercepted API: {Name} ({Count} variants)", apiResult.Name, apiResult.Variants.Count);
                    return apiResult;
                }
            }

            // Strategy 3: Embedded State
            var stateResult = TryParseEmbeddedState(html, url);
            if (stateResult is not null && stateResult.Variants.Count > 0)
            {
                _logger.LogInformation("Penti: Parsed via Embedded State: {Name} ({Count} variants)", stateResult.Name, stateResult.Variants.Count);
                return stateResult;
            }

            // Strategy 4: DOM
            var domResult = await TryParseDomAsync(page, url);
            if (domResult is not null && domResult.Variants.Count > 0)
            {
                _logger.LogInformation("Penti: Parsed via DOM: {Name} ({Count} variants)", domResult.Name, domResult.Variants.Count);
                return domResult;
            }

            if (jsonLdResult is not null && !string.IsNullOrWhiteSpace(jsonLdResult.Name))
            {
                return jsonLdResult;
            }

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

        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Penti Ürün" : "Penti Ürün";

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

            var name = root.TryGetProperty("name", out var np) ? np.GetString() ?? "Penti Ürün" : "Penti Ürün";

            var variants = new List<VariantAvailabilityDto>();

            // 1. variantOptions (Hybris)
            if (root.TryGetProperty("variantOptions", out var vOptions) && vOptions.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in vOptions.EnumerateArray())
                {
                    string? sName = null;
                    if (opt.TryGetProperty("variantOptionQualifiers", out var quals) && quals.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var q in quals.EnumerateArray())
                        {
                            if (q.TryGetProperty("qualifier", out var qp) && qp.GetString() == "size" &&
                                q.TryGetProperty("value", out var vp))
                            {
                                sName = vp.GetString();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(sName) && opt.TryGetProperty("code", out var cp))
                        sName = cp.GetString();

                    var isInStock = false;
                    if (opt.TryGetProperty("stock", out var sp))
                    {
                        if (sp.TryGetProperty("stockLevelStatus", out var slsp))
                            isInStock = slsp.GetString()?.Equals("inStock", StringComparison.OrdinalIgnoreCase) == true;
                        else if (sp.TryGetProperty("stockLevel", out var slp) && slp.TryGetInt32(out var sl))
                            isInStock = sl > 0;
                    }

                    if (!string.IsNullOrWhiteSpace(sName))
                    {
                        variants.Add(new VariantAvailabilityDto(sName.Trim(), isInStock));
                    }
                }
            }
            // 2. sizes array
            else if (root.TryGetProperty("sizes", out var sizesElem) && sizesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sizesElem.EnumerateArray())
                {
                    var sName = s.TryGetProperty("name", out var snp) ? snp.GetString()
                              : s.TryGetProperty("size", out var sp) ? sp.GetString() : null;

                    var isAvail = s.TryGetProperty("inStock", out var isp) ? isp.GetBoolean()
                                : s.TryGetProperty("stock", out var stp) && stp.TryGetInt32(out var st) && st > 0;

                    if (!string.IsNullOrWhiteSpace(sName))
                    {
                        variants.Add(new VariantAvailabilityDto(sName.Trim(), isAvail));
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

    public ProductInspectResponse? TryParseEmbeddedState(string html, string url)
    {
        var patterns = new[]
        {
            @"window\.__INITIAL_STATE__\s*=\s*(\{.*?\});",
            @"window\.productDetail\s*=\s*(\{.*?\});",
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
            const h1 = document.querySelector('h1.product-title, h1.product-name, h1, [data-testid=""product-name""]');
            return h1 ? h1.textContent.trim() : '';
        }");

        if (string.IsNullOrWhiteSpace(name)) return null;

        var imageUrl = await page.EvaluateAsync<string?>(@"() => {
            const img = document.querySelector('.product-images img, .image-box img, img.product-image');
            return img ? (img.src || img.getAttribute('data-src')) : null;
        }");

        var variantsJson = await page.EvaluateAsync<string>(@"() => {
            const results = [];
            const sizeButtons = document.querySelectorAll(
                'button.size-box, button[data-size], .size-options button, .product-size button, .size-selector button, .sizes button, [aria-label*=""Beden""]'
            );

            for (const btn of sizeButtons) {
                const text = btn.innerText || btn.textContent || btn.getAttribute('data-size') || '';
                const cleanText = text.replace(/(\r\n|\n|\r)/gm, ' ').trim();
                if (!cleanText || cleanText.length > 25) continue;

                const isDisabled = btn.disabled || 
                                   btn.classList.contains('disabled') || 
                                   btn.classList.contains('out-of-stock') || 
                                   btn.classList.contains('passive') ||
                                   btn.classList.contains('empty') ||
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
        if (string.IsNullOrWhiteSpace(title)) return "Penti Ürün";
        var clean = Regex.Replace(title, @"\s*[-|]\s*Penti.*$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? title.Trim() : clean;
    }
}
