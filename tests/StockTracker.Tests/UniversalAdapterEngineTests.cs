using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Infrastructure.Adapters;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class UniversalAdapterEngineTests
{
    private readonly Mock<IBrowserService> _browserMock = new();
    private readonly Mock<ILogger<ZaraAdapter>> _zaraLoggerMock = new();
    private readonly Mock<ILogger<MangoAdapter>> _mangoLoggerMock = new();
    private readonly Mock<ILogger<ProductInspectService>> _inspectLoggerMock = new();
    private readonly Mock<ILogger<ProductsController>> _controllerLoggerMock = new();

    private readonly ZaraAdapter _zaraAdapter;
    private readonly MangoAdapter _mangoAdapter;
    private readonly StoreAdapterRegistry _registry;

    public UniversalAdapterEngineTests()
    {
        _zaraAdapter = new ZaraAdapter(_browserMock.Object, _zaraLoggerMock.Object);
        _mangoAdapter = new MangoAdapter(_browserMock.Object, _mangoLoggerMock.Object);
        _registry = new StoreAdapterRegistry(new IStoreAdapter[] { _zaraAdapter, _mangoAdapter });
    }

    // ── 1. Registry & URL Resolution Tests ──────────────────────────────────

    [Theory]
    [InlineData("https://www.zara.com/tr/tr/100-keten-ince-ceket-p08281012.html", "Zara")]
    [InlineData("https://zara.com/es/en/blazer-p123.html", "Zara")]
    [InlineData("HTTPS://WWW.ZARA.COM/TR/TR/PRODUCT-P999.HTML", "Zara")]
    [InlineData("https://www.zara.com/tr/tr/product-p123.html?v1=123&utm_source=test#details", "Zara")]
    [InlineData("https://shop.mango.com/tr/tr/kadin/elbise-ve-tulumlar/uzun-elbise_12345.html", "Mango")]
    [InlineData("https://mango.com/es/es/women/jackets_999.html", "Mango")]
    [InlineData("HTTPS://SHOP.MANGO.COM/ES/WOMEN/ITEM-P1", "Mango")]
    [InlineData("https://shop.mango.com/tr/p?color=99&size=M#top", "Mango")]
    public void StoreAdapterRegistry_ResolvesCorrectAdapter_ForKnownStoreUrls(string url, string expectedStore)
    {
        var adapter = _registry.Resolve(url);

        Assert.NotNull(adapter);
        Assert.Equal(expectedStore, adapter.StoreName);
    }

    [Theory]
    [InlineData("https://www.bershka.com/tr/product-p1")]
    [InlineData("https://www.pullandbear.com/tr/product-p2")]
    [InlineData("https://www.hm.com/tr/product-p3")]
    [InlineData("https://www.google.com")]
    [InlineData("https://notzara.com/zara-product")]
    [InlineData("https://notmango.com/mango-product")]
    [InlineData("not-a-valid-url")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void StoreAdapterRegistry_ReturnsNull_ForUnsupportedOrInvalidUrls(string? url)
    {
        var adapter = _registry.Resolve(url!);
        Assert.Null(adapter);
    }

    [Fact]
    public void StoreAdapterRegistry_GetSupportedStores_ReturnsAllConfiguredStores()
    {
        var stores = _registry.GetSupportedStores();

        Assert.Equal(2, stores.Count);
        Assert.Contains(stores, s => s.Name == "Zara" && s.AdapterKey == "zara" && s.Domains.Contains("zara.com"));
        Assert.Contains(stores, s => s.Name == "Mango" && s.AdapterKey == "mango" && s.Domains.Contains("shop.mango.com"));
    }

    // ── 2. Mango Adapter Unit Tests ─────────────────────────────────────────

    [Theory]
    [InlineData("https://shop.mango.com/tr/tr/kadin/elbise_123.html", true)]
    [InlineData("https://mango.com/es/es/women/item.html", true)]
    [InlineData("https://www.mango.com/us/men/jacket.html", true)]
    [InlineData("https://www.zara.com/tr/tr/product.html", false)]
    [InlineData("https://bershka.com/item", false)]
    public void MangoAdapter_CanHandle_ValidatesUrlCorrectly(string url, bool expected)
    {
        var result = _mangoAdapter.CanHandle(url);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MangoAdapter_TryExtractFromJsonLd_ParsesProductVariantsCorrectly()
    {
        var jsonLdHtml = @"
        <html>
        <head>
          <script type=""application/ld+json"">
          {
            ""@context"": ""https://schema.org/"",
            ""@type"": ""Product"",
            ""name"": ""Keten Gömlek Elbise"",
            ""image"": ""https://st.mngbcn.com/photos/item.jpg"",
            ""offers"": [
              {
                ""@type"": ""Offer"",
                ""name"": ""XS"",
                ""availability"": ""https://schema.org/InStock""
              },
              {
                ""@type"": ""Offer"",
                ""name"": ""S"",
                ""availability"": ""https://schema.org/InStock""
              },
              {
                ""@type"": ""Offer"",
                ""name"": ""M"",
                ""availability"": ""https://schema.org/OutOfStock""
              },
              {
                ""@type"": ""Offer"",
                ""name"": ""L"",
                ""availability"": ""https://schema.org/InStock""
              }
            ]
          }
          </script>
        </head>
        </html>";

        var result = _mangoAdapter.TryExtractFromJsonLd(jsonLdHtml, "https://shop.mango.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Mango", result.Store);
        Assert.Equal("Keten Gömlek Elbise", result.Name);
        Assert.Equal("https://st.mngbcn.com/photos/item.jpg", result.ImageUrl);
        Assert.Equal(4, result.Variants.Count);

        var xs = result.Variants.First(v => v.Name == "XS");
        Assert.True(xs.Available);

        var m = result.Variants.First(v => v.Name == "M");
        Assert.False(m.Available);
    }

    [Fact]
    public void MangoAdapter_TryExtractFromJsonLd_ParsesOneSizeBagProductCorrectly()
    {
        var bagJsonLdHtml = @"
        <html>
        <head>
          <meta property=""og:image"" content=""https://st.mngbcn.com/photos/bag.jpg"" />
          <script type=""application/ld+json"">
          {
            ""@context"": ""https://schema.org/"",
            ""@type"": ""Product"",
            ""name"": ""Orta boy city klapalı çanta - Kadın"",
            ""offers"": {
              ""@type"": ""Offer"",
              ""price"": ""1499.99"",
              ""priceCurrency"": ""TRY"",
              ""availability"": ""https://schema.org/InStock""
            }
          }
          </script>
        </head>
        </html>";

        var result = _mangoAdapter.TryExtractFromJsonLd(bagJsonLdHtml, "https://shop.mango.com/tr/tr/p/kadin/canta/orta-boy-city-klapali-canta/17055819/99/00");

        Assert.NotNull(result);
        Assert.Equal("Mango", result.Store);
        Assert.Equal("Orta boy city klapalı çanta - Kadın", result.Name);
        Assert.Equal("https://st.mngbcn.com/photos/bag.jpg", result.ImageUrl);
        Assert.Single(result.Variants);
        Assert.Equal("Standart", result.Variants[0].Name);
        Assert.True(result.Variants[0].Available);
    }

    // ── 3. Products Controller & Universal API Tests ────────────────────────

    [Fact]
    public void ProductsController_GetSupportedStores_ReturnsOkWithStoreList()
    {
        var inspectService = new ProductInspectService(_registry, _inspectLoggerMock.Object);
        var productService = new ProductService(_registry, new Mock<ILogger<ProductService>>().Object);
        var controller = new ProductsController(productService, inspectService, _registry, _controllerLoggerMock.Object);

        var actionResult = controller.GetSupportedStores();

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var stores = Assert.IsAssignableFrom<IReadOnlyList<StoreInfo>>(okResult.Value);
        Assert.Equal(2, stores.Count);
        Assert.Contains(stores, s => s.Name == "Zara");
        Assert.Contains(stores, s => s.Name == "Mango");
    }

    [Fact]
    public async Task ProductsController_InspectProduct_WhenStoreUnsupported_ReturnsUnprocessableEntityWithStandardCode()
    {
        var inspectService = new ProductInspectService(_registry, _inspectLoggerMock.Object);
        var productService = new ProductService(_registry, new Mock<ILogger<ProductService>>().Object);
        var controller = new ProductsController(productService, inspectService, _registry, _controllerLoggerMock.Object);

        var request = new FetchProductRequest("https://www.bershka.com/tr/unsupported-item-p123.html");
        var actionResult = await controller.InspectProduct(request, CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(actionResult);
        var json = JsonSerializer.Serialize(unprocessable.Value);

        Assert.Contains("UNSUPPORTED_STORE", json);
        Assert.Contains("desteklenmiyor", json, StringComparison.OrdinalIgnoreCase);
    }
}
