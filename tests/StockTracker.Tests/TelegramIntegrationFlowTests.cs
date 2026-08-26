using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;

namespace StockTracker.Tests;

public class TelegramIntegrationFlowTests
{
    [Fact]
    public async Task FullTelegramNotificationFlow_Baseline_ThenStockArrival_ThenSubsequentCheck()
    {
        var repoMock = new Mock<IStockMonitorRepository>();
        var resolverMock = new Mock<IStoreAdapterResolver>();
        var notificationServiceMock = new Mock<INotificationService>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:RequestTimeoutSeconds", "10" }
            })
            .Build();

        var checker = new StockCheckerService(
            resolverMock.Object,
            repoMock.Object,
            notificationServiceMock.Object,
            config,
            new Mock<ILogger<StockCheckerService>>().Object);

        var monitor = new StockMonitor
        {
            Id = 55,
            ProductUrl = "https://www.zara.com/tr/tr/100-keten-ince-ceket-p08281012.html",
            Store = "Zara",
            ProductName = "%100 KETEN İNCE CEKET",
            ImageUrl = "https://static.zara.net/photo.jpg",
            SelectedVariants = new List<string> { "M (US M)" },
            ProtectedTelegramBotToken = "ENC_TEST_TOKEN",
            TelegramChatId = "123456789",
            CheckIntervalMinutes = 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var adapterMock = new Mock<IInspectableAdapter>();
        resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        // ────────────────────────────────────────────────────────────────────
        // STEP 1: Initial Baseline Check (M is currently out of stock)
        // ────────────────────────────────────────────────────────────────────
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN İNCE CEKET",
                ImageUrl: "https://static.zara.net/photo.jpg",
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: false) }
            ));

        var initialChanges = await checker.CheckMonitorAsync(monitor);

        // Verify: Baseline recorded in DB, NO notifications sent
        Assert.Empty(initialChanges);
        Assert.Single(monitor.VariantStates);
        Assert.False(monitor.VariantStates.First().IsAvailable);
        notificationServiceMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Never);

        // ────────────────────────────────────────────────────────────────────
        // STEP 2: Stock Arrives! (M becomes true)
        // ────────────────────────────────────────────────────────────────────
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN İNCE CEKET",
                ImageUrl: "https://static.zara.net/photo.jpg",
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: true) }
            ));

        notificationServiceMock.Setup(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var arrivalChanges = await checker.CheckMonitorAsync(monitor);

        // Verify: Exactly 1 stock change detected, exactly 1 Telegram notification sent!
        Assert.Single(arrivalChanges);
        Assert.Equal("M (US M)", arrivalChanges[0].VariantName);
        Assert.False(arrivalChanges[0].PreviousAvailability);
        Assert.True(arrivalChanges[0].CurrentAvailability);

        notificationServiceMock.Verify(n => n.NotifyStockAvailableAsync(
            It.Is<StockAvailableNotification>(n =>
                n.MonitorId == 55 &&
                n.VariantName == "M (US M)" &&
                n.ProductName == "%100 KETEN İNCE CEKET" &&
                n.ProductUrl == monitor.ProductUrl &&
                n.TelegramChatId == "123456789"),
            It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(monitor.LastNotifiedAt);
        Assert.Equal("M (US M)", monitor.LastNotifiedVariant);

        // ────────────────────────────────────────────────────────────────────
        // STEP 3: Subsequent Check (M is still true)
        // ────────────────────────────────────────────────────────────────────
        var subsequentChanges = await checker.CheckMonitorAsync(monitor);

        // Verify: 0 changes, NO additional notification sent!
        Assert.Empty(subsequentChanges);
        notificationServiceMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Once); // Still only once from Step 2
    }
}
