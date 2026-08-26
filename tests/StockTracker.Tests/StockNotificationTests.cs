using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class StockNotificationTests
{
    private readonly Mock<IStoreAdapterResolver> _resolverMock = new();
    private readonly Mock<IStockMonitorRepository> _repoMock = new();
    private readonly Mock<INotificationService> _notificationMock = new();
    private readonly Mock<ISecretProtector> _secretProtectorMock = new();
    private readonly Mock<ILogger<StockCheckerService>> _checkerLoggerMock = new();
    private readonly Mock<ILogger<TelegramNotificationService>> _tgLoggerMock = new();
    private readonly IConfiguration _config;

    public StockNotificationTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:RequestTimeoutSeconds", "10" },
                { "Telegram:TimeoutSeconds", "5" }
            })
            .Build();

        _secretProtectorMock.Setup(s => s.Protect(It.IsAny<string>()))
            .Returns<string>(s => $"ENC_{s}");
        _secretProtectorMock.Setup(s => s.Unprotect(It.IsAny<string>()))
            .Returns<string>(s => s.Replace("ENC_", ""));
    }

    private StockCheckerService CreateCheckerService()
    {
        return new StockCheckerService(
            _resolverMock.Object,
            _repoMock.Object,
            _notificationMock.Object,
            _config,
            _checkerLoggerMock.Object);
    }

    private static StockMonitor CreateSampleMonitor(string variant = "M")
    {
        return new StockMonitor
        {
            Id = 10,
            ProductUrl = "https://www.zara.com/tr/tr/100-keten-ceket-p08281012.html",
            Store = "Zara",
            ProductName = "%100 KETEN CEKET",
            ImageUrl = "https://static.zara.net/photo.jpg",
            SelectedVariants = new List<string> { variant },
            ProtectedTelegramBotToken = "ENC_SECRET_BOT_TOKEN_999",
            TelegramChatId = "123456789",
            CheckIntervalMinutes = 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            NextCheckAt = DateTime.UtcNow
        };
    }

    // ── 1. State Transitions & Notification Triggering ──────────────────────

    [Fact]
    public async Task CheckMonitorAsync_WhenFalseToTrue_TriggersTelegramNotification()
    {
        var service = CreateCheckerService();
        var monitor = CreateSampleMonitor("M");

        // Previous state: M = false
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 10,
            VariantName = "M",
            IsAvailable = false,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: M is now true (Stok Geldi!)
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN CEKET",
                ImageUrl: "https://static.zara.net/photo.jpg",
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: true) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);
        _notificationMock.Setup(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        Assert.False(changes[0].PreviousAvailability);
        Assert.True(changes[0].CurrentAvailability);

        // Verify Notification was sent with exact monitor data
        _notificationMock.Verify(n => n.NotifyStockAvailableAsync(
            It.Is<StockAvailableNotification>(notif =>
                notif.MonitorId == 10 &&
                notif.VariantName == "M" &&
                notif.Store == "Zara" &&
                notif.ProductName == "%100 KETEN CEKET" &&
                notif.TelegramChatId == "123456789" &&
                notif.ProtectedTelegramBotToken == "ENC_SECRET_BOT_TOKEN_999"
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        // Verify history was recorded
        _repoMock.Verify(r => r.AddNotificationHistoryAsync(
            It.Is<StockNotificationHistory>(h => h.StockMonitorId == 10 && h.VariantName == "M" && h.Success),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task CheckMonitorAsync_WhenInitialBaselineCheck_DoesNotSendTelegramNotification()
    {
        var service = CreateCheckerService();
        var monitor = CreateSampleMonitor("M"); // No existing VariantStates (initial check)

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: M is already true at initial baseline
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN CEKET",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: true) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Empty(changes);
        _notificationMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        _repoMock.Verify(r => r.AddNotificationHistoryAsync(It.IsAny<StockNotificationHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckMonitorAsync_WhenTrueToTrue_DoesNotSendNotification()
    {
        var service = CreateCheckerService();
        var monitor = CreateSampleMonitor("M");
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 10,
            VariantName = "M",
            IsAvailable = true,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN CEKET",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: true) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Empty(changes);
        _notificationMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckMonitorAsync_WhenTrueToFalse_DoesNotSendNotification()
    {
        var service = CreateCheckerService();
        var monitor = CreateSampleMonitor("M");
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 10,
            VariantName = "M",
            IsAvailable = true,
            LastCheckedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        // Scraped: M is now false (Tükendi)
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN CEKET",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M (US M)", Available: false) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        Assert.True(changes[0].PreviousAvailability);
        Assert.False(changes[0].CurrentAvailability);

        // Notification must NOT be sent for out-of-stock
        _notificationMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 2. Duplicate Prevention ─────────────────────────────────────────────

    [Fact]
    public async Task CheckMonitorAsync_WhenRecentNotificationExists_SuppressesDuplicate()
    {
        var service = CreateCheckerService();
        var monitor = CreateSampleMonitor("M");
        monitor.VariantStates.Add(new StockMonitorVariantState
        {
            StockMonitorId = 10,
            VariantName = "M",
            IsAvailable = false
        });

        var adapterMock = new Mock<IInspectableAdapter>();
        adapterMock.Setup(a => a.InspectAsync(monitor.ProductUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductInspectResponse(
                Store: "Zara",
                Name: "%100 KETEN CEKET",
                ImageUrl: null,
                Url: monitor.ProductUrl,
                Variants: new List<VariantAvailabilityDto> { new("M", Available: true) }
            ));

        _resolverMock.Setup(r => r.Resolve(monitor.ProductUrl)).Returns(adapterMock.Object);

        // Simulate recent notification already recorded in DB
        _repoMock.Setup(r => r.HasRecentNotificationAsync(10, "M", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changes = await service.CheckMonitorAsync(monitor);

        Assert.Single(changes);
        // Duplicate suppressed -> notification service not called
        _notificationMock.Verify(n => n.NotifyStockAvailableAsync(It.IsAny<StockAvailableNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 3. Telegram API Fallback & Failure Isolation ────────────────────────

    [Fact]
    public async Task TelegramNotificationService_WhenSendPhotoFails_FallsBackToSendMessage()
    {
        var httpHandlerMock = new Mock<HttpMessageHandler>();

        // sendPhoto fails with 400
        httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsoluteUri.Contains("sendPhoto")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("{\"ok\":false,\"description\":\"Wrong remote file identifier\"}")
            });

        // sendMessage succeeds with 200
        httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsoluteUri.Contains("sendMessage")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":100}}")
            });

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new TelegramNotificationService(
            factoryMock.Object,
            _secretProtectorMock.Object,
            _config,
            _tgLoggerMock.Object);

        var notification = new StockAvailableNotification(
            MonitorId: 1,
            Store: "Zara",
            ProductName: "Keten Ceket",
            ProductUrl: "https://zara.com/p1",
            ImageUrl: "https://invalid-img.net/photo.jpg",
            VariantName: "M",
            ProtectedTelegramBotToken: "ENC_TOKEN_123",
            TelegramChatId: "987654"
        );

        var result = await service.NotifyStockAvailableAsync(notification);

        Assert.True(result);
        httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.AbsoluteUri.Contains("sendMessage")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task TelegramNotificationService_EscapesHtmlSpecialCharactersInMessage()
    {
        var httpHandlerMock = new Mock<HttpMessageHandler>();
        string? capturedBody = null;

        httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync(ct);
                }
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":100}}")
                };
            });

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new TelegramNotificationService(
            factoryMock.Object,
            _secretProtectorMock.Object,
            _config,
            _tgLoggerMock.Object);

        var notification = new StockAvailableNotification(
            MonitorId: 1,
            Store: "Zara & Bershka <TR>",
            ProductName: "100% Keten Ceket <Limited>",
            ProductUrl: "https://zara.com/p1?ref=1&code=2",
            ImageUrl: null,
            VariantName: "M <Special>",
            ProtectedTelegramBotToken: "ENC_TOKEN_123",
            TelegramChatId: "987654"
        );

        var result = await service.NotifyStockAvailableAsync(notification);

        Assert.True(result);
        Assert.NotNull(capturedBody);

        using var jsonDoc = JsonDocument.Parse(capturedBody);
        var text = jsonDoc.RootElement.GetProperty("text").GetString();

        Assert.NotNull(text);
        Assert.Contains("&lt;Limited&gt;", text);
        Assert.Contains("&lt;Special&gt;", text);
        Assert.Contains("&amp;", text);
        Assert.DoesNotContain("<Limited>", text);
    }
}
