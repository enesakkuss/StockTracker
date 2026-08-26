using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Application.Services;

/// <summary>
/// Handles product fetching operations by delegating to the appropriate store adapter.
/// </summary>
public class ProductService
{
    private readonly IStoreAdapterResolver _adapterResolver;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IStoreAdapterResolver adapterResolver, ILogger<ProductService> logger)
    {
        _adapterResolver = adapterResolver;
        _logger = logger;
    }

    public async Task<ProductDto?> FetchProductAsync(string url, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching product from URL: {Url}", url);

        var adapter = _adapterResolver.Resolve(url);
        if (adapter is null)
        {
            _logger.LogWarning("No adapter found for URL: {Url}", url);
            return null;
        }

        var product = await adapter.FetchProductAsync(url, cancellationToken);
        if (product is null) return null;

        return new ProductDto(
            product.Id,
            product.Url,
            product.Name,
            product.ImageUrl,
            product.Brand,
            product.StoreType,
            product.CreatedAt,
            product.Variants.Select(v => new ProductVariantDto(v.Id, v.Size, v.Sku, v.IsInStock)).ToList()
        );
    }
}
