using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Application.Services;

/// <summary>
/// Orchestrates product inspection: resolves the right adapter and returns a clean response with inspect status and user message.
/// </summary>
public class ProductInspectService
{
    private readonly IStoreAdapterResolver _resolver;
    private readonly ILogger<ProductInspectService> _logger;

    public ProductInspectService(
        IStoreAdapterResolver resolver,
        ILogger<ProductInspectService> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the adapter for the URL and fetches product + variant data.
    /// Throws <see cref="NotSupportedException"/> if no adapter handles the URL.
    /// </summary>
    public async Task<ProductInspectResponse> InspectAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inspect request for URL: {Url}", url);

        var adapter = _resolver.Resolve(url);
        if (adapter is null)
            throw new NotSupportedException("Bu URL için desteklenen bir mağaza bulunamadı.");

        try
        {
            // If adapter supports the richer inspect contract, prefer it
            if (adapter is IInspectableAdapter inspectable)
            {
                var inspected = await inspectable.InspectAsync(url, cancellationToken);
                var status = inspected.Variants.Count > 0 ? "success" : "incomplete";
                var userMessage = status == "success"
                    ? "Ürün ve beden stokları başarıyla alındı."
                    : "Ürün bulundu ancak beden stok bilgisi şu anda alınamadı.";
                return inspected with { InspectStatus = status, UserMessage = userMessage };
            }

            // Generic fallback via IStoreAdapter.FetchProductAsync → map to DTO
            var product = await adapter.FetchProductAsync(url, cancellationToken);
            if (product is null)
            {
                return new ProductInspectResponse(
                    Store: adapter.StoreType,
                    Name: "Bilinmeyen Ürün",
                    ImageUrl: null,
                    Url: url,
                    Variants: Array.Empty<VariantAvailabilityDto>(),
                    InspectStatus: "not_found",
                    UserMessage: "Ürün artık mağazada bulunamıyor."
                );
            }

            var variants = product.Variants
                .Select(v => new VariantAvailabilityDto(v.Size, v.IsInStock))
                .ToList();

            var inspectStatus = variants.Count > 0 ? "success" : "incomplete";
            var message = inspectStatus == "success"
                ? "Ürün ve beden stokları başarıyla alındı."
                : "Ürün bulundu ancak beden stok bilgisi şu anda alınamadı.";

            return new ProductInspectResponse(
                Store: adapter.StoreType,
                Name: product.Name,
                ImageUrl: product.ImageUrl,
                Url: product.Url,
                Variants: variants,
                InspectStatus: inspectStatus,
                UserMessage: message
            );
        }
        catch (Exception ex) when (IsBotProtectionOrBlocked(ex.Message))
        {
            _logger.LogWarning("Access blocked during inspect for URL {Url}: {Message}", url, ex.Message);
            return new ProductInspectResponse(
                Store: adapter.StoreType,
                Name: "Erişim Engellendi (Bot Protection)",
                ImageUrl: null,
                Url: url,
                Variants: Array.Empty<VariantAvailabilityDto>(),
                InspectStatus: "blocked",
                UserMessage: "Mağaza şu anda ürünü kontrol etmemize izin vermiyor. Daha sonra tekrar deneyin."
            );
        }
        catch (Exception ex) when (IsNotFound(ex.Message))
        {
            _logger.LogWarning("Product not found during inspect for URL {Url}: {Message}", url, ex.Message);
            return new ProductInspectResponse(
                Store: adapter.StoreType,
                Name: "Ürün Bulunamadı",
                ImageUrl: null,
                Url: url,
                Variants: Array.Empty<VariantAvailabilityDto>(),
                InspectStatus: "not_found",
                UserMessage: "Ürün artık mağazada bulunamıyor."
            );
        }
    }

    private static bool IsBotProtectionOrBlocked(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        var lower = message.ToLowerInvariant();
        return lower.Contains("403") ||
               lower.Contains("429") ||
               lower.Contains("cloudflare") ||
               lower.Contains("access denied") ||
               lower.Contains("bot protection") ||
               lower.Contains("blocked") ||
               lower.Contains("security check");
    }

    private static bool IsNotFound(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        var lower = message.ToLowerInvariant();
        return lower.Contains("404") ||
               lower.Contains("not found") ||
               lower.Contains("bulunamadı");
    }
}
