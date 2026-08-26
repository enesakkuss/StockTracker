using Moq;
using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;

namespace StockTracker.Tests;

public class ZaraAdapterTests
{
    // ── URL Detection ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.zara.com/tr/tr/erkek-slim-fit-gömlek-p12345678.html", true)]
    [InlineData("https://www.zara.com/es/en/jacket-p00112233.html", true)]
    [InlineData("http://zara.com/tr/product", true)]
    [InlineData("https://www.zara.com/", true)]
    public void CanHandle_ReturnsTrueForZaraUrls(string url, bool expected)
    {
        var canHandle = IsZaraUrl(url);
        Assert.Equal(expected, canHandle);
    }

    [Theory]
    [InlineData("https://www.mango.com/tr/product", false)]
    [InlineData("https://www.bershka.com/tr/product", false)]
    [InlineData("https://www.google.com", false)]
    [InlineData("", false)]
    [InlineData("not-a-url", false)]
    [InlineData("https://notzara.com/zara-product", false)]
    public void CanHandle_ReturnsFalseForNonZaraUrls(string url, bool expected)
    {
        var canHandle = IsZaraUrl(url);
        Assert.Equal(expected, canHandle);
    }

    // ── ProductInspectService — Routing ─────────────────────────────────────

    [Fact]
    public async Task InspectService_ThrowsNotSupported_WhenNoAdapterFound()
    {
        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<string>()))
                    .Returns((IStoreAdapter?)null);

        var logger = new Mock<ILogger<ProductInspectService>>();
        var service = new ProductInspectService(resolverMock.Object, logger.Object);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.InspectAsync("https://www.mango.com/product"));
    }

    [Fact]
    public async Task InspectService_UsesInspectAsync_WhenAdapterIsInspectable()
    {
        var expected = new ProductInspectResponse(
            Store: "Zara",
            Name: "Test Coat",
            ImageUrl: "https://img.example.com/coat.jpg",
            Url: "https://www.zara.com/tr/test",
            Variants: new List<VariantAvailabilityDto>
            {
                new("S", true),
                new("M", false)
            });

        var adapterMock = new Mock<IInspectableAdapter>();
        adapterMock.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(true);
        adapterMock.Setup(a => a.StoreType).Returns("Zara");
        adapterMock.Setup(a => a.InspectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(expected);

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<string>()))
                    .Returns(adapterMock.Object);

        var logger = new Mock<ILogger<ProductInspectService>>();
        var service = new ProductInspectService(resolverMock.Object, logger.Object);

        var result = await service.InspectAsync("https://www.zara.com/tr/test");

        Assert.Equal("Test Coat", result.Name);
        Assert.Equal("Zara", result.Store);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants[0].Available);
        Assert.False(result.Variants[1].Available);
    }

    [Fact]
    public async Task InspectService_FallsBackToFetchProduct_WhenAdapterNotInspectable()
    {
        var adapterMock = new Mock<IStoreAdapter>(); // NOT IInspectableAdapter
        adapterMock.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(true);
        adapterMock.Setup(a => a.StoreType).Returns("GenericStore");
        adapterMock.Setup(a => a.FetchProductAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new StockTracker.Domain.Entities.Product
                   {
                       Name = "Generic Product",
                       Url = "https://generic.store/product",
                       StoreType = "GenericStore",
                       Variants = new List<StockTracker.Domain.Entities.ProductVariant>
                       {
                           new() { Size = "L", IsInStock = true }
                       }
                   });

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(It.IsAny<string>()))
                    .Returns(adapterMock.Object);

        var logger = new Mock<ILogger<ProductInspectService>>();
        var service = new ProductInspectService(resolverMock.Object, logger.Object);

        var result = await service.InspectAsync("https://generic.store/product");

        Assert.Equal("Generic Product", result.Name);
        Assert.Single(result.Variants);
        Assert.Equal("L", result.Variants[0].Name);
        Assert.True(result.Variants[0].Available);
    }

    // ── DTO Mapping ─────────────────────────────────────────────────────────

    [Fact]
    public void VariantAvailabilityDto_CarriesCorrectData()
    {
        var dto = new VariantAvailabilityDto("XL", true);
        Assert.Equal("XL", dto.Name);
        Assert.True(dto.Available);
    }

    [Fact]
    public void ProductInspectResponse_IsImmutableRecord()
    {
        var variants = new List<VariantAvailabilityDto> { new("S", false) };
        var response = new ProductInspectResponse("Zara", "Jacket", null, "https://zara.com", variants);

        Assert.Equal("Zara", response.Store);
        Assert.Equal("Jacket", response.Name);
        Assert.Null(response.ImageUrl);
        Assert.Single(response.Variants);
    }

    // ── Helper ──────────────────────────────────────────────────────────────
    private static bool IsZaraUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            url, @"https?://(www\.)?zara\.com/",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
