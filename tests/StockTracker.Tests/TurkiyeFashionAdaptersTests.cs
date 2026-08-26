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

public class TurkiyeFashionAdaptersTests
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
    private readonly MaviAdapter _maviAdapter;
    private readonly HmAdapter _hmAdapter;
    private readonly KotonAdapter _kotonAdapter;
    private readonly LcWaikikiAdapter _lcWaikikiAdapter;
    private readonly DefactoAdapter _defactoAdapter;
    private readonly PentiAdapter _pentiAdapter;

    public TurkiyeFashionAdaptersTests()
    {
        _zaraAdapter = new ZaraAdapter(_browserMock.Object, new Mock<ILogger<ZaraAdapter>>().Object);
        _mangoAdapter = new MangoAdapter(_browserMock.Object, new Mock<ILogger<MangoAdapter>>().Object);
        _pullAndBearAdapter = new PullAndBearAdapter(_browserMock.Object, new Mock<ILogger<PullAndBearAdapter>>().Object);
        _bershkaAdapter = new BershkaAdapter(_browserMock.Object, new Mock<ILogger<BershkaAdapter>>().Object);
        _stradivariusAdapter = new StradivariusAdapter(_browserMock.Object, new Mock<ILogger<StradivariusAdapter>>().Object);
        _massimoDuttiAdapter = new MassimoDuttiAdapter(_browserMock.Object, new Mock<ILogger<MassimoDuttiAdapter>>().Object);
        _oyshoAdapter = new OyshoAdapter(_browserMock.Object, new Mock<ILogger<OyshoAdapter>>().Object);
        _maviAdapter = new MaviAdapter(_browserMock.Object, new Mock<ILogger<MaviAdapter>>().Object);
        _hmAdapter = new HmAdapter(_browserMock.Object, new Mock<ILogger<HmAdapter>>().Object);
        _kotonAdapter = new KotonAdapter(_browserMock.Object, new Mock<ILogger<KotonAdapter>>().Object);
        _lcWaikikiAdapter = new LcWaikikiAdapter(_browserMock.Object, new Mock<ILogger<LcWaikikiAdapter>>().Object);
        _defactoAdapter = new DefactoAdapter(_browserMock.Object, new Mock<ILogger<DefactoAdapter>>().Object);
        _pentiAdapter = new PentiAdapter(_browserMock.Object, new Mock<ILogger<PentiAdapter>>().Object);

        _registry = new StoreAdapterRegistry(new IStoreAdapter[]
        {
            _zaraAdapter,
            _mangoAdapter,
            _pullAndBearAdapter,
            _bershkaAdapter,
            _stradivariusAdapter,
            _massimoDuttiAdapter,
            _oyshoAdapter,
            _maviAdapter,
            _hmAdapter,
            _kotonAdapter,
            _lcWaikikiAdapter,
            _defactoAdapter,
            _pentiAdapter
        });
    }

    // ── 1. Registry Resolution for All 13 Stores ────────────────────────────

    [Theory]
    [InlineData("https://www.zara.com/tr/tr/100-keten-ince-ceket-p08281012.html", "Zara")]
    [InlineData("https://shop.mango.com/tr/tr/kadin/pantolon_87093278", "Mango")]
    [InlineData("https://www.pullandbear.com/tr/erkek/giyim/ceket-c1030004501.html", "Pull&Bear")]
    [InlineData("https://www.bershka.com/tr/kadin/giyim/elbise-c1010193213.html", "Bershka")]
    [InlineData("https://www.stradivarius.com/tr/kadin/giyim/triko-kazak-c1020040502.html", "Stradivarius")]
    [InlineData("https://www.massimodutti.com/tr/100-saf-keten-gomlek-l00120101", "Massimo Dutti")]
    [InlineData("https://www.oysho.com/tr/kadin/spor/tayt-c1010045012.html", "Oysho")]
    [InlineData("https://www.mavi.com/marcus-vintage-jean-pantolon-0042432029", "Mavi")]
    [InlineData("https://mavi.com/kadin/jean/c/1?page=1", "Mavi")]
    [InlineData("HTTPS://WWW.MAVI.COM/TR/PRODUCT-P1", "Mavi")]
    [InlineData("https://www2.hm.com/tr_tr/productpage.1123456001.html", "H&M")]
    [InlineData("https://www.hm.com/tr/product/123.html", "H&M")]
    [InlineData("HTTPS://HM.COM/ES/PRODUCT-P1", "H&M")]
    [InlineData("https://www.koton.com/pamuklu-slim-fit-gomlek-3wam20002ow-000/", "Koton")]
    [InlineData("https://koton.com/kadin-elbise-p123.html", "Koton")]
    [InlineData("HTTPS://WWW.KOTON.COM/TR/P1", "Koton")]
    [InlineData("https://www.lcwaikiki.com/tr-TR/TR/urun/LC-WAIKIKI/erkek/Tisort/6543210/3123456", "LC Waikiki")]
    [InlineData("https://lcwaikiki.com/es/product-p1", "LC Waikiki")]
    [InlineData("HTTPS://WWW.LCWAIKIKI.COM/TR/P1", "LC Waikiki")]
    [InlineData("https://www.defacto.com.tr/regular-fit-polo-yaka-tisort-2856401", "DeFacto")]
    [InlineData("https://defacto.com.tr/kadin/pantolon-p1", "DeFacto")]
    [InlineData("HTTPS://WWW.DEFACTO.COM.TR/TR/P1", "DeFacto")]
    [InlineData("https://www.penti.com/tr/p/siyah-termal-kulotlu-corap-pntcorap123", "Penti")]
    [InlineData("https://penti.com/es/product-p1", "Penti")]
    [InlineData("HTTPS://WWW.PENTI.COM/TR/P1", "Penti")]
    public void StoreAdapterRegistry_ResolvesAllThirteenStoresCorrectly(string url, string expectedStore)
    {
        var adapter = _registry.Resolve(url);
        Assert.NotNull(adapter);
        Assert.Equal(expectedStore, adapter.StoreName);
    }

    [Fact]
    public void StoreAdapterRegistry_GetSupportedStores_ReturnsAllThirteenStores()
    {
        var stores = _registry.GetSupportedStores();
        Assert.Equal(13, stores.Count);

        var names = stores.Select(s => s.Name).ToList();
        var expected = new[]
        {
            "Zara", "Mango", "Pull&Bear", "Bershka", "Stradivarius", "Massimo Dutti", "Oysho",
            "Mavi", "H&M", "Koton", "LC Waikiki", "DeFacto", "Penti"
        };

        foreach (var store in expected)
        {
            Assert.Contains(store, names);
        }
    }

    [Fact]
    public void ProductsController_GetSupportedStores_ReturnsOkWithThirteenStores()
    {
        var inspectService = new ProductInspectService(_registry, new Mock<ILogger<ProductInspectService>>().Object);
        var productService = new ProductService(_registry, new Mock<ILogger<ProductService>>().Object);
        var controller = new ProductsController(productService, inspectService, _registry, new Mock<ILogger<ProductsController>>().Object);

        var actionResult = controller.GetSupportedStores();

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var stores = Assert.IsAssignableFrom<IReadOnlyList<StoreInfo>>(okResult.Value);

        Assert.Equal(13, stores.Count);
    }

    // ── 2. Mavi Adapter Unit Tests ──────────────────────────────────────────

    [Fact]
    public void MaviAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Marcus Vintage Jean Pantolon"",
          ""image"": ""https://sky-static.mavi.com/photos/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""30/32"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""32/32"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""34/32"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _maviAdapter.TryParseJsonLd(html, "https://www.mavi.com/p1");

        Assert.NotNull(result);
        Assert.Equal("Mavi", result.Store);
        Assert.Equal("Marcus Vintage Jean Pantolon", result.Name);
        Assert.Equal("https://sky-static.mavi.com/photos/item.jpg", result.ImageUrl);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "30/32").Available);
        Assert.False(result.Variants.First(v => v.Name == "34/32").Available);
    }

    [Fact]
    public void MaviAdapter_TryParseInterceptedJson_ExtractsHybrisVariantOptions()
    {
        var apiJson = @"{
          ""name"": ""Jake Slim Jean"",
          ""variantOptions"": [
            {
              ""code"": ""001"",
              ""stock"": { ""stockLevelStatus"": ""inStock"" },
              ""variantOptionQualifiers"": [
                { ""qualifier"": ""size"", ""value"": ""31/30"" }
              ]
            },
            {
              ""code"": ""002"",
              ""stock"": { ""stockLevelStatus"": ""outOfStock"" },
              ""variantOptionQualifiers"": [
                { ""qualifier"": ""size"", ""value"": ""32/30"" }
              ]
            }
          ]
        }";

        var result = _maviAdapter.TryParseInterceptedJson(apiJson, "https://www.mavi.com/p2");

        Assert.NotNull(result);
        Assert.Equal("Mavi", result.Store);
        Assert.Equal("Jake Slim Jean", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "31/30").Available);
        Assert.False(result.Variants.First(v => v.Name == "32/30").Available);
    }

    // ── 3. H&M Adapter Unit Tests ───────────────────────────────────────────

    [Fact]
    public void HmAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Slim Fit Pamuklu Gömlek"",
          ""image"": ""https://lp2.hm.com/hmgoepprod?set=source[/item.jpg]"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""L"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _hmAdapter.TryParseJsonLd(html, "https://www2.hm.com/tr_tr/productpage.1123456001.html");

        Assert.NotNull(result);
        Assert.Equal("H&M", result.Store);
        Assert.Equal("Slim Fit Pamuklu Gömlek", result.Name);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S").Available);
        Assert.False(result.Variants.First(v => v.Name == "L").Available);
    }

    [Fact]
    public void HmAdapter_TryParseInterceptedJson_ExtractsVariants()
    {
        var apiJson = @"{
          ""name"": ""Oversized Hoodie"",
          ""variants"": [
            { ""sizeFilter"": ""XS"", ""inStock"": true },
            { ""sizeFilter"": ""S"", ""inStock"": false }
          ]
        }";

        var result = _hmAdapter.TryParseInterceptedJson(apiJson, "https://www2.hm.com/p1");

        Assert.NotNull(result);
        Assert.Equal("H&M", result.Store);
        Assert.Equal("Oversized Hoodie", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants[0].Available);
        Assert.False(result.Variants[1].Available);
    }

    // ── 4. Koton Adapter Unit Tests ─────────────────────────────────────────

    [Fact]
    public void KotonAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Düğmeli Triko Hırka"",
          ""image"": ""https://koton.akinoncdn.com/products/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S-M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""L-XL"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _kotonAdapter.TryParseJsonLd(html, "https://www.koton.com/p1");

        Assert.NotNull(result);
        Assert.Equal("Koton", result.Store);
        Assert.Equal("Düğmeli Triko Hırka", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S-M").Available);
        Assert.False(result.Variants.First(v => v.Name == "L-XL").Available);
    }

    // ── 5. LC Waikiki Adapter Unit Tests ────────────────────────────────────

    [Fact]
    public void LcWaikikiAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Bisiklet Yaka Basic Tişört"",
          ""image"": ""https://img-lcwaikiki.mncdn.com/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""L"", ""availability"": ""https://schema.org/OutOfStock"" },
            { ""@type"": ""Offer"", ""name"": ""XL"", ""availability"": ""https://schema.org/InStock"" }
          ]
        }
        </script></head></html>";

        var result = _lcWaikikiAdapter.TryParseJsonLd(html, "https://www.lcwaikiki.com/tr/p1");

        Assert.NotNull(result);
        Assert.Equal("LC Waikiki", result.Store);
        Assert.Equal("Bisiklet Yaka Basic Tişört", result.Name);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "M").Available);
        Assert.False(result.Variants.First(v => v.Name == "L").Available);
        Assert.True(result.Variants.First(v => v.Name == "XL").Available);
    }

    // ── 6. DeFacto Adapter Unit Tests ───────────────────────────────────────

    [Fact]
    public void DefactoAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Polo Yaka Kısa Kollu Tişört"",
          ""image"": ""https://dfcdn.defacto.com.tr/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""XXL"", ""availability"": ""https://schema.org/OutOfStock"" }
          ]
        }
        </script></head></html>";

        var result = _defactoAdapter.TryParseJsonLd(html, "https://www.defacto.com.tr/p1");

        Assert.NotNull(result);
        Assert.Equal("DeFacto", result.Store);
        Assert.Equal("Polo Yaka Kısa Kollu Tişört", result.Name);
        Assert.Equal(3, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S").Available);
        Assert.False(result.Variants.First(v => v.Name == "XXL").Available);
    }

    // ── 7. Penti Adapter Unit Tests ─────────────────────────────────────────

    [Fact]
    public void PentiAdapter_TryParseJsonLd_ExtractsVariantsAndAvailability()
    {
        var html = @"
        <html><head>
        <script type=""application/ld+json"">
        {
          ""@context"": ""https://schema.org/"",
          ""@type"": ""Product"",
          ""name"": ""Termal Külotlu Çorap"",
          ""image"": ""https://penti.akinoncdn.com/item.jpg"",
          ""offers"": [
            { ""@type"": ""Offer"", ""name"": ""S-M"", ""availability"": ""https://schema.org/InStock"" },
            { ""@type"": ""Offer"", ""name"": ""L-XL"", ""availability"": ""https://schema.org/InStock"" }
          ]
        }
        </script></head></html>";

        var result = _pentiAdapter.TryParseJsonLd(html, "https://www.penti.com/p1");

        Assert.NotNull(result);
        Assert.Equal("Penti", result.Store);
        Assert.Equal("Termal Külotlu Çorap", result.Name);
        Assert.Equal(2, result.Variants.Count);
        Assert.True(result.Variants.First(v => v.Name == "S-M").Available);
    }

    // ── 8. Zero Fake Data Guarantee ─────────────────────────────────────────

    [Fact]
    public void Adapters_WhenNoVariantsFoundInJsonLd_NeverInventFakeVariants()
    {
        var emptyHtml = "<html><head><title>Test</title></head><body>No product here</body></html>";

        Assert.Null(_maviAdapter.TryParseJsonLd(emptyHtml, "https://www.mavi.com/p"));
        Assert.Null(_hmAdapter.TryParseJsonLd(emptyHtml, "https://hm.com/p"));
        Assert.Null(_kotonAdapter.TryParseJsonLd(emptyHtml, "https://koton.com/p"));
        Assert.Null(_lcWaikikiAdapter.TryParseJsonLd(emptyHtml, "https://lcwaikiki.com/p"));
        Assert.Null(_defactoAdapter.TryParseJsonLd(emptyHtml, "https://defacto.com.tr/p"));
        Assert.Null(_pentiAdapter.TryParseJsonLd(emptyHtml, "https://penti.com/p"));
    }
}
