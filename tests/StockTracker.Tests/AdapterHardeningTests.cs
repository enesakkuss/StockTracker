using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Adapters;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class AdapterHardeningTests
{
    private readonly Mock<IBrowserService> _browserMock = new();
    private readonly StoreAdapterRegistry _registry;

    private readonly MaviAdapter _maviAdapter;
    private readonly HmAdapter _hmAdapter;
    private readonly KotonAdapter _kotonAdapter;
    private readonly LcWaikikiAdapter _lcWaikikiAdapter;
    private readonly DefactoAdapter _defactoAdapter;
    private readonly PentiAdapter _pentiAdapter;
    private readonly ZaraAdapter _zaraAdapter;
    private readonly MangoAdapter _mangoAdapter;
    private readonly IConfiguration _config;

    public AdapterHardeningTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:RequestTimeoutSeconds", "10" },
                { "Telegram:TimeoutSeconds", "5" }
            })
            .Build();

        _zaraAdapter = new ZaraAdapter(_browserMock.Object, new Mock<ILogger<ZaraAdapter>>().Object);
        _mangoAdapter = new MangoAdapter(_browserMock.Object, new Mock<ILogger<MangoAdapter>>().Object);
        _maviAdapter = new MaviAdapter(_browserMock.Object, new Mock<ILogger<MaviAdapter>>().Object);
        _hmAdapter = new HmAdapter(_browserMock.Object, new Mock<ILogger<HmAdapter>>().Object);
        _kotonAdapter = new KotonAdapter(_browserMock.Object, new Mock<ILogger<KotonAdapter>>().Object);
        _lcWaikikiAdapter = new LcWaikikiAdapter(_browserMock.Object, new Mock<ILogger<LcWaikikiAdapter>>().Object);
        _defactoAdapter = new DefactoAdapter(_browserMock.Object, new Mock<ILogger<DefactoAdapter>>().Object);
        _pentiAdapter = new PentiAdapter(_browserMock.Object, new Mock<ILogger<PentiAdapter>>().Object);

        _registry = new StoreAdapterRegistry(new IStoreAdapter[]
        {
            _zaraAdapter, _mangoAdapter, _maviAdapter, _hmAdapter, _kotonAdapter, _lcWaikikiAdapter, _defactoAdapter, _pentiAdapter
        });
    }

    // ── 1. Malformed JSON Resilience ────────────────────────────────────────

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("<html><body>Not JSON</body></html>")]
    [InlineData("{\"@type\": \"Product\"}")] // Product with no name and no variants
    public void Adapters_WhenGivenMalformedOrEmptyJson_DoNotThrowUnhandledExceptions(string malformedContent)
    {
        Assert.Null(_maviAdapter.TryParseJsonLd(malformedContent, "https://www.mavi.com/p"));
        Assert.Null(_maviAdapter.TryParseInterceptedJson(malformedContent, "https://www.mavi.com/p"));
        Assert.Null(_hmAdapter.TryParseJsonLd(malformedContent, "https://www.hm.com/p"));
        Assert.Null(_hmAdapter.TryParseInterceptedJson(malformedContent, "https://www.hm.com/p"));
        Assert.Null(_kotonAdapter.TryParseJsonLd(malformedContent, "https://www.koton.com/p"));
        Assert.Null(_kotonAdapter.TryParseInterceptedJson(malformedContent, "https://www.koton.com/p"));
        Assert.Null(_lcWaikikiAdapter.TryParseJsonLd(malformedContent, "https://www.lcwaikiki.com/p"));
        Assert.Null(_lcWaikikiAdapter.TryParseInterceptedJson(malformedContent, "https://www.lcwaikiki.com/p"));
        Assert.Null(_defactoAdapter.TryParseJsonLd(malformedContent, "https://www.defacto.com.tr/p"));
        Assert.Null(_defactoAdapter.TryParseInterceptedJson(malformedContent, "https://www.defacto.com.tr/p"));
        Assert.Null(_pentiAdapter.TryParseJsonLd(malformedContent, "https://www.penti.com/p"));
        Assert.Null(_pentiAdapter.TryParseInterceptedJson(malformedContent, "https://www.penti.com/p"));
    }

    // ── 2. Multi-Layer Fallback: JSON-LD -> Intercepted API ─────────────────

    [Fact]
    public void MaviAdapter_WhenJsonLdHasNoVariants_InterceptedApiProvidesVariants()
    {
        var jsonLdWithoutVariants = @"
        <html><head>
        <script type=""application/ld+json"">
        { ""@type"": ""Product"", ""name"": ""Mavi Jean"" }
        </script></head></html>";

        var jsonLdResult = _maviAdapter.TryParseJsonLd(jsonLdWithoutVariants, "https://www.mavi.com/p");
        Assert.Null(jsonLdResult);

        var apiJson = @"{
          ""name"": ""Mavi Jean"",
          ""variantOptions"": [
            {
              ""stock"": { ""stockLevelStatus"": ""inStock"" },
              ""variantOptionQualifiers"": [{ ""qualifier"": ""size"", ""value"": ""32/32"" }]
            }
          ]
        }";

        var apiResult = _maviAdapter.TryParseInterceptedJson(apiJson, "https://www.mavi.com/p");
        Assert.NotNull(apiResult);
        Assert.Single(apiResult.Variants);
        Assert.Equal("32/32", apiResult.Variants[0].Name);
        Assert.True(apiResult.Variants[0].Available);
    }

    // ── 3. Zero Fake Data Guarantee: Accurate Availability Signals ──────────

    [Fact]
    public void HmAdapter_AccuratelyDistinguishes_InStockVsOutOfStock()
    {
        var apiJson = @"{
          ""name"": ""Basic T-Shirt"",
          ""variants"": [
            { ""sizeFilter"": ""S"", ""inStock"": true },
            { ""sizeFilter"": ""M"", ""inStock"": false },
            { ""sizeFilter"": ""L"", ""inStock"": true }
          ]
        }";

        var result = _hmAdapter.TryParseInterceptedJson(apiJson, "https://www.hm.com/p");
        Assert.NotNull(result);
        Assert.Equal(3, result.Variants.Count);

        Assert.True(result.Variants[0].Available);
        Assert.False(result.Variants[1].Available);
        Assert.True(result.Variants[2].Available);
    }

    // ── 4. End-to-End StockChecker State Transition: false -> true ──────────

    [Fact]
    public async Task StockChecker_WhenStateChangesFromFalseToTrue_TriggersNotification()
    {
        var monitor = new StockMonitor
        {
            Id = 1,
            ProductUrl = "https://www.mavi.com/marcus-jean",
            Store = "Mavi",
            ProductName = "Marcus Jean",
            SelectedVariants = new List<string> { "32/32" },
            ProtectedTelegramBotToken = "ENC_SECRET_BOT_TOKEN",
            TelegramChatId = "123456789",
            CheckIntervalMinutes = 5,
            IsActive = true,
            VariantStates = new List<StockMonitorVariantState>
            {
                new()
                {
                    StockMonitorId = 1,
                    VariantName = "32/32",
                    IsAvailable = false, // Previously Out of Stock
                    LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
                }
            }
        };

        var repoMock = new Mock<IStockMonitorRepository>();
        repoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(monitor);

        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle(monitor.ProductUrl)).Returns(true);
        var inspectableMock = adapterMock.As<IInspectableAdapter>();
        inspectableMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Mavi",
                Name: "Marcus Jean",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("32/32", Available: true) }
            ));

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(inspectableMock.Object);

        var notifServiceMock = new Mock<INotificationService>();
        notifServiceMock.Setup(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var checker = new StockCheckerService(
            resolverMock.Object,
            repoMock.Object,
            notifServiceMock.Object,
            _config,
            new Mock<ILogger<StockCheckerService>>().Object
        );

        var changes = await checker.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        Assert.Equal("32/32", changes[0].VariantName);
        Assert.False(changes[0].PreviousAvailability);
        Assert.True(changes[0].CurrentAvailability);

        // Verify Telegram notification was triggered
        notifServiceMock.Verify(n => n.NotifyStockAvailableAsync(
            It.Is<StockAvailableNotification>(r =>
                r.Store == "Mavi" &&
                r.VariantName == "32/32" &&
                r.TelegramChatId == "123456789"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StockChecker_WhenBaselineCheck_DoesNotTriggerNotification()
    {
        var monitor = new StockMonitor
        {
            Id = 2,
            ProductUrl = "https://www.koton.com/gomlek-1",
            Store = "Koton",
            ProductName = "Gömlek",
            SelectedVariants = new List<string> { "M" },
            ProtectedTelegramBotToken = "ENC_SECRET_BOT_TOKEN",
            TelegramChatId = "123456789",
            CheckIntervalMinutes = 5,
            IsActive = true,
            VariantStates = new List<StockMonitorVariantState>() // No baseline state yet!
        };

        var repoMock = new Mock<IStockMonitorRepository>();
        repoMock.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(monitor);

        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle(monitor.ProductUrl)).Returns(true);
        var inspectableMock = adapterMock.As<IInspectableAdapter>();
        inspectableMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Koton",
                Name: "Gömlek",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M", Available: true) }
            ));

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(inspectableMock.Object);

        var notifServiceMock = new Mock<INotificationService>();

        var checker = new StockCheckerService(
            resolverMock.Object,
            repoMock.Object,
            notifServiceMock.Object,
            _config,
            new Mock<ILogger<StockCheckerService>>().Object
        );

        var changes = await checker.CheckMonitorAsync(monitor);

        // Baseline records state, returns 0 changes, and DOES NOT send Telegram notification
        Assert.Empty(changes);
        notifServiceMock.Verify(n => n.NotifyStockAvailableAsync(
            It.IsAny<StockAvailableNotification>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
