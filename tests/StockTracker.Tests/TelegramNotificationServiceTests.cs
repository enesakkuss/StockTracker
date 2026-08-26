using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class TelegramNotificationServiceTests
{
    private readonly Mock<ILogger<TelegramNotificationService>> _loggerMock = new();
    private readonly Mock<ISecretProtector> _secretProtectorMock = new();
    private readonly IConfiguration _config;

    public TelegramNotificationServiceTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Telegram:TimeoutSeconds", "5" }
            })
            .Build();

        _secretProtectorMock.Setup(s => s.Protect(It.IsAny<string>()))
            .Returns<string>(s => $"ENC_{s}");
        _secretProtectorMock.Setup(s => s.Unprotect(It.IsAny<string>()))
            .Returns<string>(s => s.Replace("ENC_", ""));
    }

    private TelegramNotificationService CreateService(IHttpClientFactory factory)
    {
        return new TelegramNotificationService(factory, _secretProtectorMock.Object, _config, _loggerMock.Object);
    }

    private IHttpClientFactory CreateMockHttpClientFactory(HttpResponseMessage? getMeResponse, HttpResponseMessage? sendMessageResponse)
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && req.RequestUri.ToString().Contains("/getMe")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(getMeResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"id\":12345,\"is_bot\":true,\"first_name\":\"TestBot\"}}")
            });

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && req.RequestUri.ToString().Contains("/sendMessage")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(sendMessageResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1}}")
            });

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        return factoryMock.Object;
    }

    [Theory]
    [InlineData("", "12345")]
    [InlineData("   ", "12345")]
    [InlineData(null, "12345")]
    public async Task TestConnectionAsync_WithEmptyBotToken_ReturnsFailure(string? token, string chatId)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var service = CreateService(factoryMock.Object);

        var result = await service.TestConnectionAsync(token!, chatId);

        Assert.False(result.Success);
        Assert.Contains("Bot Token", result.Message);
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("123456:ABC-DEF", "")]
    [InlineData("123456:ABC-DEF", "   ")]
    [InlineData("123456:ABC-DEF", null)]
    public async Task TestConnectionAsync_WithEmptyChatId_ReturnsFailure(string token, string? chatId)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var service = CreateService(factoryMock.Object);

        var result = await service.TestConnectionAsync(token, chatId!);

        Assert.False(result.Success);
        Assert.Contains("Chat ID", result.Message);
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenGetMeFails_ReturnsFailure()
    {
        var badGetMe = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}")
        };

        var factory = CreateMockHttpClientFactory(badGetMe, null);
        var service = CreateService(factory);

        var result = await service.TestConnectionAsync("INVALID_TOKEN", "123456789");

        Assert.False(result.Success);
        Assert.Contains("geçersiz", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSendMessageFails_ChatNotFound_ReturnsHelpfulError()
    {
        var badSend = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: chat not found\"}")
        };

        var factory = CreateMockHttpClientFactory(null, badSend);
        var service = CreateService(factory);

        var result = await service.TestConnectionAsync("123456:VALID_TOKEN", "999999999");

        Assert.False(result.Success);
        Assert.Contains("Chat ID bulunamadı", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenTelegramTimesOut_ReturnsTimeoutMessage()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var service = CreateService(factoryMock.Object);

        var result = await service.TestConnectionAsync("123456:VALID_TOKEN", "123456789");

        Assert.False(result.Success);
        Assert.Contains("zaman aşımı", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSuccessful_ReturnsTrue()
    {
        var factory = CreateMockHttpClientFactory(null, null);
        var service = CreateService(factory);

        var result = await service.TestConnectionAsync("123456:VALID_TOKEN", "123456789");

        Assert.True(result.Success);
        Assert.Equal("Telegram bağlantısı başarılı.", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_NeverLeaksBotTokenInResponse()
    {
        const string secretToken = "SECRET_1234567890_VERY_SENSITIVE_BOT_TOKEN";

        var badGetMe = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"{{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized with {secretToken}\"}}")
        };

        var factory = CreateMockHttpClientFactory(badGetMe, null);
        var service = CreateService(factory);

        var result = await service.TestConnectionAsync(secretToken, "123456789");

        Assert.DoesNotContain(secretToken, result.Message);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(secretToken, json);
    }

    // ── Controller Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task TelegramController_TestConnection_ReturnsOkWithServiceResult()
    {
        var telegramServiceMock = new Mock<ITelegramService>();
        telegramServiceMock
            .Setup(s => s.TestConnectionAsync("token123", "chat123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramTestResponse(true, "Telegram bağlantısı başarılı."));

        var controllerLogger = new Mock<ILogger<TelegramController>>();
        var controller = new TelegramController(telegramServiceMock.Object, controllerLogger.Object);

        var request = new TelegramTestRequest("token123", "chat123");
        var actionResult = await controller.TestConnection(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<TelegramTestResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("Telegram bağlantısı başarılı.", response.Message);
    }

    [Theory]
    [InlineData("", "chat123")]
    [InlineData("token123", "")]
    public async Task TelegramController_WithEmptyInputs_ReturnsBadRequest(string token, string chatId)
    {
        var telegramServiceMock = new Mock<ITelegramService>();
        var controllerLogger = new Mock<ILogger<TelegramController>>();
        var controller = new TelegramController(telegramServiceMock.Object, controllerLogger.Object);

        var request = new TelegramTestRequest(token, chatId);
        var actionResult = await controller.TestConnection(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult);
        var response = Assert.IsType<TelegramTestResponse>(badRequestResult.Value);
        Assert.False(response.Success);
    }
}
