using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Persistence;
using StockTracker.Infrastructure.Services;

using Microsoft.Data.Sqlite;

namespace StockTracker.Tests;

public class SubscriptionAndMonetizationTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public SubscriptionAndMonetizationTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sqliteConnection.Dispose();
    }

    private AppDbContext CreateContext() => new(_dbOptions);

    private SubscriptionPlan CreateFreePlan() => new()
    {
        Id = 1,
        Name = "FREE",
        Description = "Free Plan",
        Price = 0m,
        Currency = "TRY",
        BillingPeriod = "Monthly",
        MaxActiveMonitors = 5,
        MaxTotalMonitors = 10,
        MinCheckIntervalMinutes = 60,
        TelegramEnabled = true,
        MaxNotificationsPerDay = 20,
        MaxInspectRequestsPerDay = 20,
        IsActive = true
    };

    private SubscriptionPlan CreatePremiumPlan() => new()
    {
        Id = 2,
        Name = "PREMIUM",
        Description = "Premium Plan",
        Price = 199m,
        Currency = "TRY",
        BillingPeriod = "Monthly",
        MaxActiveMonitors = 100,
        MaxTotalMonitors = 500,
        MinCheckIntervalMinutes = 5,
        TelegramEnabled = true,
        MaxNotificationsPerDay = 1000,
        MaxInspectRequestsPerDay = 500,
        IsActive = true
    };

    // ── 1. Plan Limits & Monitor Enforcement Tests ───────────────────────────

    [Fact]
    public async Task UsageLimitService_WhenFreeUserExceedsActiveLimit_DeniesCreation()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan();
        context.SubscriptionPlans.Add(freePlan);

        var user = new User { Id = 1, Email = "u1@test.com", FirstName = "A", LastName = "B" };
        context.Users.Add(user);

        // Add 5 active monitors for user 1 (Free limit is 5)
        for (int i = 1; i <= 5; i++)
        {
            context.StockMonitors.Add(new StockMonitor
            {
                Id = i,
                UserId = 1,
                Store = "Zara",
                ProductUrl = $"https://zara.com/p{i}",
                ProductName = $"Product {i}",
                IsActive = true
            });
        }
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        // Attempting to create 6th active monitor
        var (allowed, errorCode, message) = await limitService.CanCreateMonitorAsync(1, 60);

        Assert.False(allowed);
        Assert.Equal("PLAN_LIMIT_REACHED", errorCode);
        Assert.Contains("aktif takip limitine", message);
    }

    [Fact]
    public async Task UsageLimitService_WhenFreeUserRequestsLowInterval_DeniesCreation()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan();
        context.SubscriptionPlans.Add(freePlan);

        var user = new User { Id = 2, Email = "u2@test.com", FirstName = "C", LastName = "D" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        // Requesting 10 minutes interval when minimum is 60 minutes
        var (allowed, errorCode, message) = await limitService.CanCreateMonitorAsync(2, 10);

        Assert.False(allowed);
        Assert.Equal("CHECK_INTERVAL_NOT_ALLOWED", errorCode);
        Assert.Contains("minimum sınırından (60 dk) küçük olamaz", message);
    }

    [Fact]
    public async Task UsageLimitService_WhenPremiumUser_AllowsHigherLimitsAnd5MinInterval()
    {
        using var context = CreateContext();
        var premiumPlan = CreatePremiumPlan();
        context.SubscriptionPlans.Add(premiumPlan);

        var user = new User { Id = 3, Email = "u3@test.com", FirstName = "E", LastName = "F" };
        context.Users.Add(user);

        // Add Active Subscription to Premium
        context.Subscriptions.Add(new Subscription
        {
            Id = 1,
            UserId = 3,
            PlanId = 2,
            Plan = premiumPlan,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTime.UtcNow
        });

        // Add 10 active monitors for user 3
        for (int i = 1; i <= 10; i++)
        {
            context.StockMonitors.Add(new StockMonitor
            {
                Id = 100 + i,
                UserId = 3,
                Store = "Zara",
                ProductUrl = $"https://zara.com/p{i}",
                ProductName = $"Product {i}",
                IsActive = true
            });
        }
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        // Premium user should be allowed to create 11th monitor with 5 min interval
        var (allowed, errorCode, _) = await limitService.CanCreateMonitorAsync(3, 5);

        Assert.True(allowed);
        Assert.Null(errorCode);
    }

    [Fact]
    public async Task UsageLimitService_PausedMonitor_DoesNotCountTowardsActiveLimit()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan();
        context.SubscriptionPlans.Add(freePlan);

        var user = new User { Id = 4, Email = "u4@test.com", FirstName = "G", LastName = "H" };
        context.Users.Add(user);

        // 4 active, 3 paused (Total 7, Active 4 < 5)
        for (int i = 1; i <= 4; i++)
        {
            context.StockMonitors.Add(new StockMonitor { Id = 200 + i, UserId = 4, Store = "Zara", ProductUrl = $"https://zara.com/p{i}", IsActive = true });
        }
        for (int i = 5; i <= 7; i++)
        {
            context.StockMonitors.Add(new StockMonitor { Id = 200 + i, UserId = 4, Store = "Zara", ProductUrl = $"https://zara.com/p{i}", IsActive = false });
        }
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        var (allowed, _, _) = await limitService.CanCreateMonitorAsync(4, 60);
        Assert.True(allowed);
    }

    [Fact]
    public async Task UsageLimitService_ResumeMonitor_EnforcesActiveLimit()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan();
        context.SubscriptionPlans.Add(freePlan);

        var user = new User { Id = 5, Email = "u5@test.com", FirstName = "I", LastName = "J" };
        context.Users.Add(user);

        // 5 active monitors
        for (int i = 1; i <= 5; i++)
        {
            context.StockMonitors.Add(new StockMonitor { Id = 300 + i, UserId = 5, Store = "Zara", ProductUrl = $"https://zara.com/p{i}", IsActive = true });
        }
        // 1 paused monitor to resume
        context.StockMonitors.Add(new StockMonitor { Id = 310, UserId = 5, Store = "Zara", ProductUrl = "https://zara.com/p10", IsActive = false });
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        var (allowed, errorCode, message) = await limitService.CanActivateMonitorAsync(5, 310);
        Assert.False(allowed);
        Assert.Equal("PLAN_LIMIT_REACHED", errorCode);
    }

    // ── 2. Daily Inspect Limit Tests ────────────────────────────────────────

    [Fact]
    public async Task UsageLimitService_WhenDailyInspectLimitExceeded_DeniesInspect()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan(); // Limit: 20
        context.SubscriptionPlans.Add(freePlan);

        var user = new User { Id = 6, Email = "u6@test.com", FirstName = "K", LastName = "L" };
        context.Users.Add(user);

        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        context.DailyUsageRecords.Add(new DailyUsageRecord
        {
            UserId = 6,
            DateKey = todayKey,
            InspectRequestsCount = 20 // Reached limit!
        });
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        var (allowed, errorCode, message) = await limitService.CanInspectProductAsync(6);

        Assert.False(allowed);
        Assert.Equal("DAILY_INSPECT_LIMIT_REACHED", errorCode);
        Assert.Contains("günlük ürün inceleme limitine", message);
    }

    // ── 3. Notification Limit Isolation Tests ───────────────────────────────

    [Fact]
    public async Task UsageLimitService_WhenUserNotificationLimitReached_DoesNotAffectOtherUsers()
    {
        using var context = CreateContext();
        var freePlan = CreateFreePlan(); // MaxNotificationsPerDay = 20
        context.SubscriptionPlans.Add(freePlan);

        var userA = new User { Id = 7, Email = "userA@test.com", FirstName = "A", LastName = "A" };
        var userB = new User { Id = 8, Email = "userB@test.com", FirstName = "B", LastName = "B" };
        context.Users.AddRange(userA, userB);

        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        context.DailyUsageRecords.Add(new DailyUsageRecord
        {
            UserId = 7,
            DateKey = todayKey,
            NotificationsCount = 20 // User A has reached daily limit
        });
        context.DailyUsageRecords.Add(new DailyUsageRecord
        {
            UserId = 8,
            DateKey = todayKey,
            NotificationsCount = 3 // User B is well under limit
        });
        await context.SaveChangesAsync();

        var repo = new SubscriptionRepository(context);
        var limitService = new UsageLimitService(repo, context, new Mock<ILogger<UsageLimitService>>().Object);

        var canUserASend = await limitService.CanSendNotificationAsync(7);
        var canUserBSend = await limitService.CanSendNotificationAsync(8);

        Assert.False(canUserASend);
        Assert.True(canUserBSend); // User B is unaffected
    }

    // ── 4. Subscriptions Controller & User Isolation Tests ───────────────────

    [Fact]
    public async Task SubscriptionsController_GetMySubscription_ReturnsUserScopedData()
    {
        var subServiceMock = new Mock<ISubscriptionService>();
        var userSubDto = new UserSubscriptionDto(
            Id: 50,
            UserId: 9,
            Plan: "PREMIUM",
            Status: "Active",
            StartedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMonths(1),
            Limits: new PlanLimitsDto(100, 500, 5, true, 1000, 500)
        );

        subServiceMock.Setup(s => s.GetUserSubscriptionAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSubDto);

        var controller = new SubscriptionsController(
            subServiceMock.Object,
            new Mock<IPaymentService>().Object,
            new Mock<ILogger<SubscriptionsController>>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "9") }, "Test"))
            }
        };

        var result = await controller.GetMySubscription(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<UserSubscriptionDto>(ok.Value);

        Assert.Equal("PREMIUM", returned.Plan);
        Assert.Equal(9, returned.UserId);
        Assert.Equal(100, returned.Limits.MaxActiveMonitors);
        subServiceMock.Verify(s => s.GetUserSubscriptionAsync(9, It.IsAny<CancellationToken>()), Times.Once);
    }
}
