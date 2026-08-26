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

public class InditexAdaptersTests
{
    private readonly Mock<IBrowserService> _browserMock = new();
    private readonly StoreAdapterRegistry _registry;

    private readonly ZaraAdapter _zaraAdapter;
    private readonly MangoAdapter _mangoAdapter;
    private readonly PullAndBearAdapter _pullAndBearAdapter;
    private readonly BershkaAdapter _bershkaAdapter;
    private readonly StradivariusAdapter _stradivariusAdapter;
    private readonly MassimoDuttiAdapter _massimoDuttiAdapter;
    private readonly OyshoAdapter _oyshoAdapter;

    public InditexAdaptersTests()
    {
        _zaraAdapter = new ZaraAdapter(_browserMock.Object, new Mock<ILogger<ZaraAdapter>>().Object);
        _mangoAdapter = new MangoAdapter(_browserMock.Object, new Mock<ILogger<MangoAdapter>>().Object);
        _pullAndBearAdapter = new PullAndBearAdapter(_browserMock.Object, new Mock<ILogger<PullAndBearAdapter>>().Object);
        _bershkaAdapter = new BershkaAdapter(_browserMock.Object, new Mock<ILogger<BershkaAdapter>>().Object);
        _stradivariusAdapter = new StradivariusAdapter(_browserMock.Object, new Mock<ILogger<StradivariusAdapter>>().Object);
        _massimoDuttiAdapter = new MassimoDuttiAdapter(_browserMock.Object, new Mock<ILogger<MassimoDuttiAdapter>>().Object);
        _oyshoAdapter = new OyshoAdapter(_browserMock.Object, new Mock<ILogger<OyshoAdapter>>().Object);

        _registry = new StoreAdapterRegistry(new IStoreAdapter[]
        {
            _zaraAdapter,
            _mangoAdapter,
            _pullAndBearAdapter,
            _bershkaAdapter,
            _stradivariusAdapter,
            _massimoDuttiAdapter,
            _oyshoAdapter
        });
    }

    // ── 1. Registry Resolution for All 7 Brands ─────────────────────────────

    [Theory]
    [InlineData("https://www.zara.com/tr/tr/100-keten-ince-ceket-p08281012.html", "Zara")]
    [InlineData("https://shop.mango.com/tr/tr/kadin/elbise_123.html", "Mango")]
    [InlineData("https://www.pullandbear.com/tr/erkek/giyim/ceket-c1030004501.html?v1=123", "Pull&Bear")]
    [InlineData("https://pullandbear.com/es/en/hoodie-p123.html", "Pull&Bear")]
    [InlineData("HTTPS://WWW.PULLANDBEAR.COM/TR/PRODUCT-P1", "Pull&Bear")]
    [InlineData("https://www.bershka.com/tr/kadin/giyim/elbise-c1010193213.html", "Bershka")]
    [InlineData("https://bershka.com/de/en/jacket-p123.html?color=900#details", "Bershka")]
    [InlineData("HTTPS://WWW.BERSHKA.COM/TR/P1", "Bershka")]
    [InlineData("https://www.stradivarius.com/tr/kadin/giyim/triko-kazak-c1020040502.html", "Stradivarius")]
    [InlineData("https://stradivarius.com/es/en/blazer-p1.html", "Stradivarius")]
    [InlineData("HTTPS://WWW.STRADIVARIUS.COM/TR/P1", "Stradivarius")]
    [InlineData("https://www.massimodutti.com/tr/100-saf-keten-gomlek-l00120101", "Massimo Dutti")]
    [InlineData("https://massimodutti.com/es/en/suit-jacket-p999.html", "Massimo Dutti")]
    [InlineData("HTTPS://WWW.MASSIMODUTTI.COM/TR/P1", "Massimo Dutti")]
    [InlineData("https://www.oysho.com/tr/kadin/spor/tayt-c1010045012.html", "Oysho")]
    [InlineData("https://oysho.com/es/en/seamless-leggings-p123.html", "Oysho")]
    [InlineData("HTTPS://WWW.OYSHO.COM/TR/P1", "Oysho")]
    public void StoreAdapterRegistry_ResolvesAllSevenStoresCorrectly(string url, string expectedStore)
    {
        var adapter = _registry.Resolve(url);
        Assert.NotNull(adapter);
        Assert.Equal(expectedStore, adapter.StoreName);
    }

    [Fact]
    public void StoreAdapterRegistry_GetSupportedStores_ReturnsSevenStores()
    {
        var stores = _registry.GetSupportedStores();
        Assert.Equal(7, stores.Count);

        var names = stores.Select(s => s.Name).ToList();
        Assert.Contains("Zara", names);
        Assert.Contains("Mango", names);
        Assert.Contains("Pull&Bear", names);
        Assert.Contains("Bershka", names);
        Assert.Contains("Stradivarius", names);
        Assert.Contains("Massimo Dutti", names);
        Assert.Contains("Oysho", names);
    }

    // ── 2. Pull&Bear Adapter Tests ──────────────────────────────────────────

    [Fact]
    public void PullAndBearAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Oversize Keten Gömlek"",
          ""image"": ""https://static.pullandbear.net/2/photos/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""M"", ""availability"": ""https://schema.org/OutOfStock"" },
            { ""@type"": ""Offer"", ""name"": ""L"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""XL"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _pullAndBearAdapter.TryParseJsonLd(html, "https://www.pullandbear.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Pull&Bear", result.Store);
        Assert.Equal("Oversize Keten Gömlek", result.Name);
        Assert.Equal("https://static.pullandbear.net/2/photos/item.jpg", result.ImageUrl);
        Assert.Equal(4, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S").Available);
        Assert.False(result.Variants.First(v => v.Name == "M").Available);
        Assert.True(result.Variants.First(v => v.Name == "L").Available);
        Assert.False(result.Variants.First(v => v.Name == "XL").Available);
    }

    [Fact]
    public void PullAndBearAdapter_TryParseInterceptedJson_ExtractsDetailSizes()
    {
        var apiJson = @"{
          ""detail"": {
            ""name"": ""Basic Şişme Yelek"",
            ""sizes"": [
              { ""name"": ""M"", ""isBuyable"": true, ""visibilityValue"": ""SHOW"" },
              { ""name"": ""L"", ""isBuyable"": false, ""visibilityValue"": ""HIDE"" }
            ]
          }
        }";

        var result = _pullAndBearAdapter.TryParseInterceptedJson(apiJson, "https://www.pullandbear.com/tr/p2");

        Assert.NotNull(result);
        Assert.Equal("Pull&Bear", result.Store);
        Assert.Equal("Basic Şişme Yelek", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "M").Available);
        Assert.False(result.Variants.First(v => v.Name == "L").Available);
    }

    [Fact]
    public void PullAndBearAdapter_TryParseInterceptedJson_ExtractsNestedColorSizes()
    {
        var apiJson = @"{
          ""name"": ""Deri Ceket"",
          ""colors"": [
            {
              ""name"": ""Siyah"",
              ""image"": { ""url"": ""https://static.pullandbear.net/deri.jpg"" },
              ""sizes"": [
                { ""name"": ""S (US S)"", ""isBuyable"": true },
                { ""name"": ""M (US M)"", ""isBuyable"": true },
                { ""name"": ""L (US L)"", ""isBuyable"": false }
              ]
            }
          ]
        }";

        var result = _pullAndBearAdapter.TryParseInterceptedJson(apiJson, "https://www.pullandbear.com/tr/p3");

        Assert.NotNull(result);
        Assert.Equal("Pull&Bear", result.Store);
        Assert.Equal("Deri Ceket", result.Name);
        Assert.Equal("https://static.pullandbear.net/deri.jpg", result.ImageUrl);
        Assert.Equal(3, result.Variants.Count);
        Assert.Equal("S (US S)", result.Variants[0].Name);
        Assert.True(result.Variants[0].Available);
        Assert.False(result.Variants[2].Available);
    }

    // ── 3. Bershka Adapter Tests ────────────────────────────────────────────

    [Fact]
    public void BershkaAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Baggy Fit Denim Pantolon"",
          ""image"": ""https://static.bershka.net/4/photos/item.jpg"",
          ""hasVariant"": [
            { ""@type"": ""Product"", ""size"": ""36"", ""offers"": { ""availability"": ""https://schema.org/InStock"" } },
            { ""@type"": ""Product"", ""size"": ""38"", ""offers"": { ""availability"": ""https://schema.org/OutOfStock"" } },
            { ""@type"": ""Product"", ""size"": ""40"", ""offers"": { ""availability"": ""https://schema.org/InStock"" } }
          ]
        }
        </script></head></html>";

        var result = _bershkaAdapter.TryParseJsonLd(html, "https://www.bershka.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Bershka", result.Store);
        Assert.Equal("Baggy Fit Denim Pantolon", result.Name);
        Assert.Equal("https://static.bershka.net/4/photos/item.jpg", result.ImageUrl);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "36").Available);
        Assert.False(result.Variants.First(v => v.Name == "38").Available);
    }

    [Fact]
    public void BershkaAdapter_TryParseInterceptedJson_ExtractsSizes()
    {
        var apiJson = @"{
          ""detail"": {
            ""name"": ""Cropped Blazer"",
            ""sizes"": [
              { ""name"": ""XS"", ""isBuyable"": true },
              { ""name"": ""S"", ""isBuyable"": true },
              { ""name"": ""M"", ""isBuyable"": false }
            ]
          }
        }";

        var result = _bershkaAdapter.TryParseInterceptedJson(apiJson, "https://www.bershka.com/tr/p2");

        Assert.NotNull(result);
        Assert.Equal("Bershka", result.Store);
        Assert.Equal("Cropped Blazer", result.Name);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "XS").Available);
        Assert.False(result.Variants.First(v => v.Name == "M").Available);
    }

    // ── 4. Stradivarius Adapter Tests ───────────────────────────────────────

    [Fact]
    public void StradivariusAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Fitilli Triko Kazak"",
          ""image"": ""https://static.stradivarius.net/5/photos/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""XS-S"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""M-L"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _stradivariusAdapter.TryParseJsonLd(html, "https://www.stradivarius.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Stradivarius", result.Store);
        Assert.Equal("Fitilli Triko Kazak", result.Name);
        Assert.Equal("https://static.stradivarius.net/5/photos/item.jpg", result.ImageUrl);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "XS-S").Available);
        Assert.False(result.Variants.First(v => v.Name == "M-L").Available);
    }

    // ── 5. Massimo Dutti Adapter Tests ──────────────────────────────────────

    [Fact]
    public void MassimoDuttiAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""%100 Kaşmir Palto"",
          ""image"": ""https://static.massimodutti.net/3/photos/palto.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""48"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""50"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""52"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _massimoDuttiAdapter.TryParseJsonLd(html, "https://www.massimodutti.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Massimo Dutti", result.Store);
        Assert.Equal("%100 Kaşmir Palto", result.Name);
        Assert.Equal("https://static.massimodutti.net/3/photos/palto.jpg", result.ImageUrl);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "48").Available);
        Assert.True(result.Variants.First(v => v.Name == "50").Available);
        Assert.False(result.Variants.First(v => v.Name == "52").Available);
    }

    // ── 6. Oysho Adapter Tests ──────────────────────────────────────────────

    [Fact]
    public void OyshoAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Dikişsiz Bilek Boyu Tayt"",
          ""image"": ""https://static.oysho.net/6/photos/tayt.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""L"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _oyshoAdapter.TryParseJsonLd(html, "https://www.oysho.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Oysho", result.Store);
        Assert.Equal("Dikişsiz Bilek Boyu Tayt", result.Name);
        Assert.Equal("https://static.oysho.net/6/photos/tayt.jpg", result.ImageUrl);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S").Available);
        Assert.True(result.Variants.First(v => v.Name == "M").Available);
        Assert.False(result.Variants.First(v => v.Name == "L").Available);
    }

    [Fact]
    public void OyshoAdapter_TryParseJsonLd_WithGraphStructure_ExtractsProductVariants()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org"",
          ""@graph"": [
            {
              ""@type"": ""BreadcrumbList"",
              ""itemListElement"": []
            },
            {
              ""@type"": ""Product"",
              ""name"": ""Spor Sütyeni"",
              ""image"": { ""@type"": ""ImageObject"", ""url"": ""https://static.oysho.net/bra.jpg"" },
              ""offers"": [
                { ""@type"": ""Offer"", ""name"": ""75B"", ""availability"": ""https://schema.org/InStock"" },
                { ""@type"": ""Offer"", ""name"": ""80B"", ""availability"": ""https://schema.org/OutOfStock"" }
              ]
            }
          ]
        }
        </script></head></html>";

        var result = _oyshoAdapter.TryParseJsonLd(html, "https://www.oysho.com/tr/bra-p1");

        Assert.NotNull(result);
        Assert.Equal("Oysho", result.Store);
        Assert.Equal("Spor Sütyeni", result.Name);
        Assert.Equal("https://static.oysho.net/bra.jpg", result.ImageUrl);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "75B").Available);
        Assert.False(result.Variants.First(v => v.Name == "80B").Available);
    }

    // ── 7. Mango Embedded State Test ────────────────────────────────────────

    [Fact]
    public void MangoAdapter_TryExtractFromEmbeddedState_ExtractsColorsAndSizes()
    {
        var html = @"
        <html><head>
        <script>
          window.__INITIAL_STATE__ = {
            ""product"": {
              ""name"": ""Keten Gömlek"",
              ""colors"": [
                {
                  ""name"": ""Beyaz"",
                  ""sizes"": [
                    { ""label"": ""S"", ""available"": true },
                    { ""label"": ""M"", ""available"": true },
                    { ""label"": ""L"", ""available"": false }
                  ]
                }
              ]
            }
          };
        </script>
        </head></html>";

        var result = _mangoAdapter.TryExtractFromEmbeddedState(html, "https://shop.mango.com/tr/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("Mango", result.Store);
        Assert.Equal("Keten Gömlek", result.Name);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants[0].Available);
        Assert.True(result.Variants[1].Available);
        Assert.False(result.Variants[2].Available);
    }

    // ── 8. ProductsController Stores Endpoint Integration ───────────────────

    [Fact]
    public void ProductsController_GetSupportedStores_ReturnsAllSevenInditexAndMangoStores()
    {
        var inspectService = new ProductInspectService(_registry, new Mock<ILogger<ProductInspectService>>().Object);
        var productService = new ProductService(_registry, new Mock<ILogger<ProductService>>().Object);
        var controller = new ProductsController(productService, inspectService, _registry, new Mock<ILogger<ProductsController>>().Object);

        var actionResult = controller.GetSupportedStores();

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var stores = Assert.IsAssignableFrom<IReadOnlyList<StoreInfo>>(okResult.Value);

        Assert.Equal(7, stores.Count);
        var expected = new[] { "Zara", "Mango", "Pull&Bear", "Bershka", "Stradivarius", "Massimo Dutti", "Oysho" };
        foreach (var name in expected)
        {
            Assert.Contains(stores, s => s.Name == name);
        }
    }
}
