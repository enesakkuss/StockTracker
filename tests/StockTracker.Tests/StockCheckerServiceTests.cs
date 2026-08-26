using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;

namespace StockTracker.Tests;

public class StockCheckerServiceTests
{
    private readonly Mock<IStoreAdapterResolver> _resolverMock = new();
    private readonly Mock<IStockMonitorRepository> _repoMock = new();
    private readonly Mock<ILogger<StockCheckerService>> _loggerMock = new();
    private readonly Mock<INotificationService> _notificationMock = new();
    private readonly IConfiguration _config;

    public StockCheckerServiceTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:RequestTimeoutSeconds", "10" }
            })
            .Build();
    }

    private StockCheckerService CreateService()
    {
        return new StockCheckerService(_resolverMock.Object, _repoMock.Object, _notificationMock.Object, _config, _loggerMock.Object);
    }

    private static StockMonitor CreateSampleMonitor(params string[] selectedVariants)
    {
        return new StockMonitor
        {
            Id = 1,
            ProductUrl = "https://www.zara.com/tr/tr/product-p1.html",
            Store = "Zara",
            ProductName = "Keten Ceket",
            SelectedVariants = selectedVariants.ToList(),
            ProtectedTelegramBotToken = "ENC_TOKEN",
            TelegramChatId = "12345",
            CheckIntervalMinutes = 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            NextCheckAt = DateTime.UtcNow
        };
    }

    // ── 1. Variant Matcher Tests ────────────────────────────────────────────

    [Theory]
    [InlineData("M", "M (US M)", true)]
    [InlineData("M (US M)", "M", true)]
    [InlineData("L", "L (US L)", true)]
    [InlineData("38", "38 (EU 38)", true)]
    [InlineData("EU 38", "38", true)]
    [InlineData("S", "M (US M)", false)]
    [InlineData("XL", "S", false)]
    public void VariantMatcher_IsMatch_EvaluatesCorrectly(string selected, string candidate, bool expected)
    {
        var result = VariantMatcher.IsMatch(selected, candidate);
        Assert.Equal(expected, result);
    }

    // ── 2. Initial Check Baseline ───────────────────────────────────────────

    [Fact]
    public async Task CheckMonitorAsync_InitialCheck_RecordsBaselineStateWithoutFiringChangeAlert()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("M", "L");

        var adapterMock = new Mock<IInspectableAdapter>();
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "Keten Ceket",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto>
                {
                    new("M (US M)", Available: true),
                    new("L (US L)", Available: false),
                    new("XL (US XL)", Available: true) // Unselected variant
                }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        // Initial check should record baseline, not trigger change alerts
        Assert.Empty(changes);
        Assert.Equal(2, monitor.VariantStates.Count);

        var mState = monitor.VariantStates.First(s => s.VariantName == "M");
        Assert.True(mState.IsAvailable);

        var lState = monitor.VariantStates.First(s => s.VariantName == "L");
        Assert.False(lState.IsAvailable);

        Assert.Equal("Success", monitor.LastCheckStatus);
        Assert.NotNull(monitor.LastCheckedAt);
        Assert.True(monitor.NextCheckAt > DateTime.UtcNow);
        _repoMock.Verify(r => r.UpdateAsync(monitor, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 3. State Transitions ────────────────────────────────────────────────

    [Fact]
    public async Task CheckMonitorAsync_WhenStockDoesNotChange_ReturnsZeroChanges()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("M");
        // Pre-existing state: M = false
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 1,
            VariantName = "M",
            IsAvailable = false,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: M is still false
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "Keten Ceket",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: false) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Empty(changes);
        Assert.False(monitor.VariantStates.First().IsAvailable);
    }

    [Fact]
    public async Task CheckMonitorAsync_WhenStockChangesFromFalseToTrue_ReturnsStockArrivalChange()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("M");
        // Pre-existing state: M = false
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 1,
            VariantName = "M",
            IsAvailable = false,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: M is now in stock!
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "Keten Ceket",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: true) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal(1, change.MonitorId);
        Assert.Equal("M", change.VariantName);
        Assert.False(change.PreviousAvailability);
        Assert.True(change.CurrentAvailability);
        Assert.False(change.IsInitialCheck);

        Assert.True(monitor.VariantStates.First().IsAvailable);
    }

    [Fact]
    public async Task CheckMonitorAsync_WhenStockChangesFromTrueToFalse_ReturnsStockDepletionChange()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("L");
        // Pre-existing state: L = true
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 1,
            VariantName = "L",
            IsAvailable = true,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: L is now out of stock!
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "Keten Ceket",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("L (US L)", Available: false) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        var change = changes[0];
        Assert.Equal("L", change.VariantName);
        Assert.True(change.PreviousAvailability);
        Assert.False(change.CurrentAvailability);
    }

    // ── 4. Fault Tolerance & Sanitization ───────────────────────────────────

    [Fact]
    public async Task CheckMonitorAsync_WhenScraperThrows_RecordsErrorWithoutCrashing()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("M");

        var adapterMock = new Mock<IInspectableAdapter>();
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Site unreachable 503"));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Empty(changes);
        Assert.Equal("Failed", monitor.LastCheckStatus);
        Assert.NotNull(monitor.LastCheckError);
        Assert.Contains("503", monitor.LastCheckError);
        _repoMock.Verify(r => r.UpdateAsync(monitor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckMonitorByIdAsync_WhenMonitorIsInactive_ThrowsInvalidOperationException()
    {
        var service = CreateService();
        var monitor = CreateSampleMonitor("M");
        monitor.IsActive = false;

        _repoMock.Setup(r => r.GetByIdWithStatesAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(monitor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckMonitorByIdAsync(1));
    }

    [Fact]
    public async Task CheckMonitorByIdAsync_WhenMonitorNotFound_ThrowsKeyNotFoundException()
    {
        var service = CreateService();
        _repoMock.Setup(r => r.GetByIdWithStatesAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((StockMonitor?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CheckMonitorByIdAsync(999));
    }
}
