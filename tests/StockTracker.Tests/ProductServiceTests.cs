using Moq;
using Microsoft.Extensions.Logging;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Adapters;

namespace StockTracker.Tests;

public class ProductServiceTests
{
    [Fact]
    public async Task FetchProductAsync_WhenNoAdapterFound_ReturnsNull()
    {
        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<string>())).Returns((IStoreAdapter?)null);

        var loggerMock = new Mock<ILogger<ProductService>>();
        var service = new ProductService(resolverMock.Object, loggerMock.Object);

        var result = await service.FetchProductAsync("https://unsupported.com/product");

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchProductAsync_WhenAdapterReturnsProduct_ReturnsMappedDto()
    {
        var product = new Product
        {
            Id = 1,
            Url = "https://www.zara.com/tr/some-product",
            Name = "Test Ürün",
            Brand = "Zara",
            StoreType = "Zara",
            CreatedAt = DateTime.UtcNow,
            Variants = new List<ProductVariant>
            {
                new ProductVariant { Id = 1, Size = "S", IsInStock = true },
                new ProductVariant { Id = 2, Size = "M", IsInStock = false }
            }
        };

        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(true);
        adapterMock.Setup(a => a.FetchProductAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(product);

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<string>())).Returns(adapterMock.Object);

        var loggerMock = new Mock<ILogger<ProductService>>();
        var service = new ProductService(resolverMock.Object, loggerMock.Object);

        var result = await service.FetchProductAsync(product.Url);

        Assert.NotNull(result);
        Assert.Equal("Test Ürün", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants[0].IsInStock);
        Assert.False(result.Variants[1].IsInStock);
    }

    [Fact]
    public void StoreAdapterResolver_ReturnsCorrectAdapter_BasedOnUrl()
    {
        var adapter1 = new Mock<IStoreAdapter>();
        adapter1.Setup(a => a.CanHandle("https://www.zara.com/tr/product")).Returns(true);
        adapter1.Setup(a => a.StoreType).Returns("Zara");

        var adapter2 = new Mock<IStoreAdapter>();
        adapter2.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(false);
        adapter2.Setup(a => a.StoreType).Returns("Mango");

        var resolver = new StoreAdapterResolver(new[] { adapter1.Object, adapter2.Object });

        var resolved = resolver.Resolve("https://www.zara.com/tr/product");

        Assert.NotNull(resolved);
        Assert.Equal("Zara", resolved.StoreType);
    }

    [Fact]
    public void StoreAdapterResolver_ReturnsNull_WhenNoAdapterMatches()
    {
        var adapter = new Mock<IStoreAdapter>();
        adapter.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(false);

        var resolver = new StoreAdapterResolver(new[] { adapter.Object });

        var resolved = resolver.Resolve("https://www.mango.com/tr/product");

        Assert.Null(resolved);
    }
}
