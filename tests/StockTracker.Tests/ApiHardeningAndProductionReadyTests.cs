using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Api.Middleware;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;

namespace StockTracker.Tests;

public class ApiHardeningAndProductionReadyTests
{
    private readonly IConfiguration _config;

    public ApiHardeningAndProductionReadyTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Monitoring:MinimumIntervalMinutes", "5" }
            })
            .Build();
    }

    // ── 1. API Response Standardization Tests ───────────────────────────────

    [Fact]
    public void ApiResponse_Ok_CreatesSuccessResponseWithData()
    {
        var data = new { Test = "Value" };
        var response = ApiResponse<object>.Ok(data, "corr-123");

        Assert.True(response.Success);
        Assert.Equal(data, response.Data);
        Assert.Null(response.Error);
        Assert.Equal("corr-123", response.CorrelationId);
    }

    [Fact]
    public void ApiResponse_Fail_CreatesErrorResponse()
    {
        var response = ApiResponse<object>.Fail("NOT_FOUND", "Item not found", null, "corr-456");

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.NotNull(response.Error);
        Assert.Equal("NOT_FOUND", response.Error.Code);
        Assert.Equal("Item not found", response.Error.Message);
        Assert.Equal("corr-456", response.CorrelationId);
    }

    // ── 2. Global Exception Middleware Tests ─────────────────────────────────

    [Fact]
    public async Task GlobalExceptionMiddleware_CatchesArgumentException_Returns400WithStandardJson()
    {
        var loggerMock = new Mock<ILogger<GlobalExceptionMiddleware>>();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new ArgumentException("Geçersiz argüman"),
            loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-ID"));

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        var result = JsonSerializer.Deserialize<ApiResponse<object>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.Error?.Code);
        Assert.Equal("Geçersiz argüman", result.Error?.Message);
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_CatchesUnhandledException_HidesStackTrace()
    {
        var loggerMock = new Mock<ILogger<GlobalExceptionMiddleware>>();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidProgramException("Sensitive internal details"),
            loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        Assert.DoesNotContain("Sensitive internal details", json);
        Assert.DoesNotContain("StackTrace", json);
    }

    // ── 3. Pagination Tests ─────────────────────────────────────────────────

    [Fact]
    public void PagedResponse_CalculatesTotalPagesAndNavigationCorrectly()
    {
        var items = new List<string> { "item1", "item2" };
        var paged = new PagedResponse<string>(items, 45, 1, 20);

        Assert.Equal(1, paged.Page);
        Assert.Equal(20, paged.PageSize);
        Assert.Equal(45, paged.TotalCount);
        Assert.Equal(3, paged.TotalPages);
        Assert.True(paged.HasNextPage);
        Assert.False(paged.HasPreviousPage);
    }

    [Fact]
    public void PaginationParams_EnforcesMaxPageSizeLimit()
    {
        var p = new PaginationParams { PageSize = 500 };
        Assert.Equal(100, p.PageSize); // Capped at 100

        p.PageSize = -10;
        Assert.Equal(1, p.PageSize); // Minimum 1
    }

    // ── 4. Product Inspect Status & Zero Fake Data Tests ─────────────────────

    [Fact]
    public async Task ProductInspectService_WhenVariantsFound_ReturnsStatusSuccess()
    {
        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle("https://www.zara.com/tr/item-p1")).Returns(true);
        adapterMock.Setup(a => a.StoreType).Returns("Zara");
        adapterMock.Setup(a => a.FetchProductAsync("https://www.zara.com/tr/item-p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product
            {
                Name = "Gömlek",
                Url = "https://www.zara.com/tr/item-p1",
                StoreType = "Zara",
                Variants = new List<ProductVariant>
                {
                    new() { Size = "M", IsInStock = true },
                    new() { Size = "L", IsInStock = false }
                }
            });

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve("https://www.zara.com/tr/item-p1")).Returns(adapterMock.Object);

        var service = new ProductInspectService(resolverMock.Object, new Mock<ILogger<ProductInspectService>>().Object);
        var result = await service.InspectAsync("https://www.zara.com/tr/item-p1");

        Assert.Equal("success", result.InspectStatus);
        Assert.Equal(2, result.Variants.Count);
        Assert.Equal("Gömlek", result.Name);
    }

    [Fact]
    public async Task ProductInspectService_When403OrCloudflare_ReturnsStatusBlockedAndNeverFakeStock()
    {
        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle("https://www.zara.com/tr/blocked")).Returns(true);
        adapterMock.Setup(a => a.StoreType).Returns("Zara");
        adapterMock.Setup(a => a.FetchProductAsync("https://www.zara.com/tr/blocked", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 403 Forbidden - Cloudflare Bot Protection"));

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve("https://www.zara.com/tr/blocked")).Returns(adapterMock.Object);

        var service = new ProductInspectService(resolverMock.Object, new Mock<ILogger<ProductInspectService>>().Object);
        var result = await service.InspectAsync("https://www.zara.com/tr/blocked");

        Assert.Equal("blocked", result.InspectStatus);
        Assert.Empty(result.Variants); // NEVER invent fake variants or fake stock!
    }

    [Fact]
    public async Task ProductInspectService_WhenNotFound_ReturnsStatusNotFound()
    {
        var adapterMock = new Mock<IStoreAdapter>();
        adapterMock.Setup(a => a.CanHandle("https://www.zara.com/tr/notfound")).Returns(true);
        adapterMock.Setup(a => a.StoreType).Returns("Zara");
        adapterMock.Setup(a => a.FetchProductAsync("https://www.zara.com/tr/notfound", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ürün bulunamadı 404"));

        var resolverMock = new Mock<IStoreAdapterResolver>();
        resolverMock.Setup(r => r.Resolve("https://www.zara.com/tr/notfound")).Returns(adapterMock.Object);

        var service = new ProductInspectService(resolverMock.Object, new Mock<ILogger<ProductInspectService>>().Object);
        var result = await service.InspectAsync("https://www.zara.com/tr/notfound");

        Assert.Equal("not_found", result.InspectStatus);
        Assert.Empty(result.Variants);
    }

    // ── 5. Monitor Pause / Resume & Update Tests ─────────────────────────────

    [Fact]
    public async Task MonitorsController_PauseAndResume_TogglesMonitoringState()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var pausedDto = new StockMonitorDto(1, "https://zara.com/p", "Zara", "Item", null, new[] { "M" }, 10, false, DateTime.UtcNow, null);
        var resumedDto = new StockMonitorDto(1, "https://zara.com/p", "Zara", "Item", null, new[] { "M" }, 10, true, DateTime.UtcNow, null);

        monitorServiceMock.Setup(s => s.StopMonitorAsync(1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pausedDto);
        monitorServiceMock.Setup(s => s.StartMonitorAsync(1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resumedDto);

        var controller = new MonitorsController(
            monitorServiceMock.Object,
            new Mock<IStockCheckerService>().Object,
            new Mock<ILogger<MonitorsController>>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "5") }, "Test"))
            }
        };

        var pauseRes = await controller.Pause(1, CancellationToken.None);
        var pauseOk = Assert.IsType<OkObjectResult>(pauseRes);
        var pauseModel = Assert.IsType<StockMonitorDto>(pauseOk.Value);
        Assert.False(pauseModel.IsActive);

        var resumeRes = await controller.Resume(1, CancellationToken.None);
        var resumeOk = Assert.IsType<OkObjectResult>(resumeRes);
        var resumeModel = Assert.IsType<StockMonitorDto>(resumeOk.Value);
        Assert.True(resumeModel.IsActive);
    }

    [Fact]
    public async Task MonitorsController_Update_UpdatesMonitorSuccessfully()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var req = new UpdateMonitorRequest(new List<string> { "L", "XL" }, 15, "123456");
        var updatedDto = new StockMonitorDto(2, "https://zara.com/p2", "Zara", "Item 2", null, new[] { "L", "XL" }, 15, true, DateTime.UtcNow, null);

        monitorServiceMock.Setup(s => s.UpdateMonitorAsync(2, 5, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var controller = new MonitorsController(
            monitorServiceMock.Object,
            new Mock<IStockCheckerService>().Object,
            new Mock<ILogger<MonitorsController>>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "5") }, "Test"))
            }
        };

        var res = await controller.Update(2, req, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(res);
        var dto = Assert.IsType<StockMonitorDto>(ok.Value);

        Assert.Equal(15, dto.CheckIntervalMinutes);
        Assert.Contains("XL", dto.SelectedVariants);
    }

    // ── 6. Notification History Query Tests ──────────────────────────────────

    [Fact]
    public async Task NotificationsController_ReturnsPagedNotificationHistory()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var sampleHistories = new List<NotificationHistoryDto>
        {
            new(1, 10, "Zara", "Keten Ceket", null, "M", false, true, DateTime.UtcNow, true, null)
        };
        var paged = new PagedResponse<NotificationHistoryDto>(sampleHistories, 1, 1, 20);

        monitorServiceMock.Setup(s => s.GetNotificationHistoriesAsync(7, It.IsAny<NotificationQueryParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var controller = new NotificationsController(monitorServiceMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test"))
            }
        };

        var result = await controller.GetNotifications(new NotificationQueryParams(), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PagedResponse<NotificationHistoryDto>>>(okResult.Value);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Single(response.Data.Items);
        Assert.Equal("Zara", response.Data.Items[0].Store);
        Assert.Equal("Keten Ceket", response.Data.Items[0].ProductName);
    }
}
