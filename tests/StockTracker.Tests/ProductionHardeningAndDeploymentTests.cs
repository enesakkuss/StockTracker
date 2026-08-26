using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Api.Middleware;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Persistence;
using StockTracker.Infrastructure.Services;
using Xunit;

namespace StockTracker.Tests;

public class ProductionHardeningAndDeploymentTests
{
    private readonly IConfiguration _config;
    private readonly PasswordHasher _passwordHasher = new();
    private readonly JwtTokenGenerator _jwtTokenGenerator;

    public ProductionHardeningAndDeploymentTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:SecretKey", "Hardening_Secret_Key_For_Unit_Testing_32_Bytes_Long_2026!" },
                { "Jwt:Issuer", "StockTrackerProduction" },
                { "Jwt:Audience", "StockTrackerAudience" }
            })
            .Build();

        _jwtTokenGenerator = new JwtTokenGenerator(_config);
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_AppliesAllStrictSecurityHeaders_AndRemovesServerHeaders()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var middleware = new SecurityHeadersMiddleware(next: (ctx) =>
        {
            ctx.Response.Headers["Server"] = "Kestrel";
            ctx.Response.Headers["X-Powered-By"] = "ASP.NET";
            return Task.CompletedTask;
        }, envMock.Object);

        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        await middleware.InvokeAsync(context);

        var typedHeaders = context.Response.Headers;

        Assert.Equal("nosniff", typedHeaders["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", typedHeaders["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", typedHeaders["Referrer-Policy"].ToString());
        Assert.Equal("0", typedHeaders["X-XSS-Protection"].ToString());
        Assert.Contains("default-src 'self'", typedHeaders["Content-Security-Policy"].ToString());
        Assert.Contains("frame-ancestors 'none'", typedHeaders["Content-Security-Policy"].ToString());
        Assert.Contains("max-age=31536000", typedHeaders["Strict-Transport-Security"].ToString());

        Assert.False(typedHeaders.ContainsKey("Server"));
        Assert.False(typedHeaders.ContainsKey("X-Powered-By"));
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_NeverLeaksStackTrace_AndIncludesCorrelationId()
    {
        var loggerMock = new Mock<ILogger<GlobalExceptionMiddleware>>();
        var middleware = new GlobalExceptionMiddleware(next: (_) =>
        {
            throw new InvalidOperationException("İşlem çakışması tespit edildi.");
        }, loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "corr-test-audit-999";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var jsonResponse = await reader.ReadToEndAsync();

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("corr-test-audit-999", context.Response.Headers["X-Correlation-ID"].ToString());

        Assert.Contains("İşlem çakışması tespit edildi.", jsonResponse);
        Assert.DoesNotContain("at StockTracker.", jsonResponse);
        Assert.DoesNotContain("System.InvalidOperationException", jsonResponse);
    }

    [Fact]
    public async Task DatabaseHealthCheck_WhenCanConnect_ReturnsHealthy()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var check = new DatabaseHealthCheck(db);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Telegram_SecretToken_IsEncryptedWithProtector_AndMaskedInSettingsDto()
    {
        var userRepoMock = new Mock<IUserRepository>();
        var protector = new DataProtectionSecretProtector(_config);
        var rawToken = "123456789:ABC_SUPER_SECRET_PRODUCTION_BOT_TOKEN";

        var user = new User
        {
            Id = 42,
            Email = "tg_user@test.com",
            ProtectedTelegramBotToken = protector.Protect(rawToken),
            TelegramChatId = "987654321",
            TelegramNotificationsEnabled = true
        };

        userRepoMock.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var telegramService = new UserTelegramService(userRepoMock.Object, protector, new Mock<ILogger<UserTelegramService>>().Object);
        var settings = await telegramService.GetTelegramSettingsAsync(42);

        Assert.True(settings.IsConfigured);
        Assert.NotNull(settings.MaskedBotToken);
        Assert.Contains("••••••", settings.MaskedBotToken);
        Assert.DoesNotContain("ABC_SUPER_SECRET", settings.MaskedBotToken);
        Assert.Equal("987654321", settings.ChatId);
    }

    [Fact]
    public async Task IdorProtection_UserCannotAccessOrMutateAnotherUsersMonitors()
    {
        var monitor1 = new StockMonitor
        {
            Id = 101,
            UserId = 1,
            ProductUrl = "https://www.zara.com/tr/tr/item-1.html",
            ProductName = "Test Item",
            Store = "Zara",
            SelectedVariants = new List<string> { "M" },
            IsActive = true
        };

        var monitorRepoMock = new Mock<IStockMonitorRepository>();
        // GetByIdAsync with userId checks ownership
        monitorRepoMock.Setup(r => r.GetByIdAsync(101, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(monitor1);
        monitorRepoMock.Setup(r => r.GetByIdAsync(101, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitor?)null);

        // DeleteAsync with userId checks ownership
        monitorRepoMock.Setup(r => r.DeleteAsync(101, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        monitorRepoMock.Setup(r => r.DeleteAsync(101, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var monitorService = new StockMonitorService(
            monitorRepoMock.Object,
            new Mock<ISecretProtector>().Object,
            new Mock<IStoreAdapterResolver>().Object,
            _config,
            new Mock<ILogger<StockMonitorService>>().Object
        );

        // User 2 tries to access User 1's monitor -> returns null
        var res = await monitorService.GetMonitorByIdAsync(101, userId: 2);
        Assert.Null(res);

        // User 2 tries to stop User 1's monitor -> returns null
        var stopped = await monitorService.StopMonitorAsync(101, userId: 2);
        Assert.Null(stopped);

        // User 2 tries to delete User 1's monitor -> returns false
        var deleted = await monitorService.DeleteMonitorAsync(101, userId: 2);
        Assert.False(deleted);
    }

    [Fact]
    public void Frontend_ConfigJs_MaintainsBillingDisabled_InProduction()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "StockTracker.Api", "wwwroot", "js", "config.js");
        if (!File.Exists(configPath))
        {
            configPath = Path.GetFullPath("src/StockTracker.Api/wwwroot/js/config.js");
        }

        if (File.Exists(configPath))
        {
            var content = File.ReadAllText(configPath);
            Assert.Contains("billingEnabled: false", content);
        }
    }
}
