using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;

namespace StockTracker.Tests;

public class StockMonitorServiceTests
{
    private readonly Mock<IStockMonitorRepository> _repoMock = new();
    private readonly Mock<ISecretProtector> _protectorMock = new();
    private readonly Mock<IStoreAdapterResolver> _resolverMock = new();
    private readonly Mock<ILogger<StockMonitorService>> _loggerMock = new();
    private readonly IConfiguration _configuration;

    public StockMonitorServiceTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Monitoring:MinimumIntervalMinutes", "5" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _protectorMock.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns<string>(s => $"ENC_{s}");
        _protectorMock.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns<string>(s => s.Replace("ENC_", ""));

        var mockAdapter = new Mock<IStoreAdapter>();
        mockAdapter.Setup(a => a.StoreName).Returns("Zara");
        mockAdapter.Setup(a => a.CanHandle(It.Is<string>(u => u.Contains("zara.com")))).Returns(true);
        _resolverMock.Setup(r => r.Resolve(It.Is<string>(u => u.Contains("zara.com")))).Returns(mockAdapter.Object);
    }

    private StockMonitorService CreateService()
    {
        return new StockMonitorService(_repoMock.Object, _protectorMock.Object, _resolverMock.Object, _configuration, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateMonitorAsync_WithValidRequest_SavesAndReturnsDto()
    {
        var service = CreateService();

        var request = new CreateMonitorRequest(
            ProductUrl: "https://www.zara.com/tr/tr/product-p123.html",
            Store: "Zara",
            ProductName: "Keten Ceket",
            ImageUrl: "https://img.zara.net/ceket.jpg",
            SelectedVariants: new[] { "M", "L" },
            TelegramBotToken: "SECRET_BOT_TOKEN_123",
            TelegramChatId: "987654321",
            CheckIntervalMinutes: 10
        );

        _repoMock.Setup(r => r.AddAsync(It.IsAny<StockMonitor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitor m, CancellationToken _) =>
            {
                m.Id = 42;
                return m;
            });

        var result = await service.CreateMonitorAsync(request);

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Keten Ceket", result.ProductName);
        Assert.Equal("Zara", result.Store);
        Assert.Equal(2, result.SelectedVariants.Count);
        Assert.True(result.IsActive);
        Assert.Equal(10, result.CheckIntervalMinutes);

        // Verify token was protected before storing
        _protectorMock.Verify(p => p.Protect("SECRET_BOT_TOKEN_123"), Times.Once);
        _repoMock.Verify(r => r.AddAsync(It.Is<StockMonitor>(m => m.ProtectedTelegramBotToken == "ENC_SECRET_BOT_TOKEN_123"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateMonitorAsync_NeverLeaksBotTokenInDtoOrSerializedJson()
    {
        var service = CreateService();
        const string secretToken = "VERY_CONFIDENTIAL_TELEGRAM_TOKEN_999";

        var request = new CreateMonitorRequest(
            ProductUrl: "https://www.zara.com/tr/product",
            Store: "Zara",
            ProductName: "Test Product",
            ImageUrl: null,
            SelectedVariants: new[] { "S" },
            TelegramBotToken: secretToken,
            TelegramChatId: "12345",
            CheckIntervalMinutes: 15
        );

        _repoMock.Setup(r => r.AddAsync(It.IsAny<StockMonitor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitor m, CancellationToken _) => { m.Id = 1; return m; });

        var dto = await service.CreateMonitorAsync(request);

        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(secretToken, json);
        Assert.DoesNotContain("ENC_", json);
    }

    [Fact]
    public async Task CreateMonitorAsync_IgnoresSpoofedStore_AndUsesAuthenticStoreFromAdapter()
    {
        var service = CreateService();

        var request = new CreateMonitorRequest(
            ProductUrl: "https://www.zara.com/tr/tr/product-p123.html",
            Store: "FakeSpoofedMango", // User tries to spoof store
            ProductName: "Keten Ceket",
            ImageUrl: null,
            SelectedVariants: new[] { "M" },
            TelegramBotToken: "SECRET_BOT_TOKEN_123",
            TelegramChatId: "987654321",
            CheckIntervalMinutes: 10
        );

        StockMonitor? savedMonitor = null;
        _repoMock.Setup(r => r.AddAsync(It.IsAny<StockMonitor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitor m, CancellationToken _) =>
            {
                savedMonitor = m;
                m.Id = 99;
                return m;
            });

        var result = await service.CreateMonitorAsync(request);

        Assert.NotNull(savedMonitor);
        Assert.Equal("Zara", savedMonitor.Store); // Derived from adapter!
        Assert.Equal("Zara", result.Store);
    }

    // ── Validations ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateMonitorAsync_WithEmptyUrl_ThrowsArgumentException(string? url)
    {
        var service = CreateService();
        var req = new CreateMonitorRequest(url!, "Zara", "Name", null, new[] { "M" }, "token", "chatId", 10);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMonitorAsync(req));
    }

    [Fact]
    public async Task CreateMonitorAsync_WithInvalidUrl_ThrowsArgumentException()
    {
        var service = CreateService();
        var req = new CreateMonitorRequest("not-a-valid-url", "Zara", "Name", null, new[] { "M" }, "token", "chatId", 10);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMonitorAsync(req));
    }

    [Fact]
    public async Task CreateMonitorAsync_WithEmptyVariants_ThrowsArgumentException()
    {
        var service = CreateService();
        var req = new CreateMonitorRequest("https://zara.com/p", "Zara", "Name", null, Array.Empty<string>(), "token", "chatId", 10);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMonitorAsync(req));
    }

    [Fact]
    public async Task CreateMonitorAsync_WithIntervalBelowMinimum_ThrowsArgumentException()
    {
        var service = CreateService();
        // Minimum is 5 min in configuration
        var req = new CreateMonitorRequest("https://zara.com/p", "Zara", "Name", null, new[] { "M" }, "token", "chatId", CheckIntervalMinutes: 2);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMonitorAsync(req));
    }

    // ── Start, Stop, Delete ─────────────────────────────────────────────────

    [Fact]
    public async Task StopMonitorAsync_SetsIsActiveToFalse()
    {
        var service = CreateService();
        var existing = new StockMonitor { Id = 1, IsActive = true, ProductName = "Test", SelectedVariants = new() { "M" } };
        _repoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await service.StopMonitorAsync(1);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        _repoMock.Verify(r => r.UpdateAsync(It.Is<StockMonitor>(m => !m.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartMonitorAsync_SetsIsActiveToTrue()
    {
        var service = CreateService();
        var existing = new StockMonitor { Id = 1, IsActive = false, ProductName = "Test", SelectedVariants = new() { "M" } };
        _repoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await service.StartMonitorAsync(1);

        Assert.NotNull(result);
        Assert.True(result.IsActive);
        _repoMock.Verify(r => r.UpdateAsync(It.Is<StockMonitor>(m => m.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteMonitorAsync_ReturnsTrueWhenFound()
    {
        var service = CreateService();
        _repoMock.Setup(r => r.DeleteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await service.DeleteMonitorAsync(1);

        Assert.True(result);
    }

    // ── Controller Integration Tests ────────────────────────────────────────

    [Fact]
    public async Task MonitorsController_GetAll_ReturnsOkWithList()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var checkerMock = new Mock<IStockCheckerService>();
        var sampleList = new List<StockMonitorDto>
        {
            new(1, "https://zara.com/p", "Zara", "Product", null, new[] { "M" }, 10, true, DateTime.UtcNow, null)
        };

        monitorServiceMock.Setup(s => s.GetPagedMonitorsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<StockMonitorDto>(sampleList, 1, 1, 20));

        var controller = new MonitorsController(monitorServiceMock.Object, checkerMock.Object, new Mock<ILogger<MonitorsController>>().Object);

        var actionResult = await controller.GetAll(new PaginationParams(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var paged = Assert.IsAssignableFrom<PagedResponse<StockMonitorDto>>(okResult.Value);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task MonitorsController_GetById_WhenNotFound_Returns404()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var checkerMock = new Mock<IStockCheckerService>();
        monitorServiceMock.Setup(s => s.GetMonitorByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitorDto?)null);
        monitorServiceMock.Setup(s => s.GetMonitorByIdAsync(999, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitorDto?)null);

        var controller = new MonitorsController(monitorServiceMock.Object, checkerMock.Object, new Mock<ILogger<MonitorsController>>().Object);

        var actionResult = await controller.GetById(999, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(actionResult);
    }
}
