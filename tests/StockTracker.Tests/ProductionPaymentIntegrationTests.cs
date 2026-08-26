using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Payments.Mock;
using StockTracker.Infrastructure.Persistence;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class ProductionPaymentIntegrationTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly IConfiguration _config;
    private readonly MockPaymentProvider _mockProvider;

    public ProductionPaymentIntegrationTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        using var context = new AppDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Payment:Provider", "Mock" },
                { "Payment:Mock:WebhookSecret", "test_mock_webhook_secret_12345" }
            })
            .Build();

        _mockProvider = new MockPaymentProvider(_config, new Mock<ILogger<MockPaymentProvider>>().Object);
    }

    public void Dispose()
    {
        _sqliteConnection.Dispose();
    }

    private AppDbContext CreateContext() => new(_dbOptions);

    private (SubscriptionPlan Free, SubscriptionPlan Premium) SeedPlans(AppDbContext context)
    {
        var free = new SubscriptionPlan
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

        var premium = new SubscriptionPlan
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

        context.SubscriptionPlans.AddRange(free, premium);
        context.SaveChanges();
        return (free, premium);
    }

    private User SeedUser(AppDbContext context, int id = 1, string email = "user@test.com")
    {
        var user = new User { Id = id, Email = email, FirstName = "Test", LastName = "User" };
        context.Users.Add(user);
        context.SaveChanges();
        return user;
    }

    private PaymentService CreatePaymentService(AppDbContext context)
    {
        var paymentRepo = new PaymentTransactionRepository(context);
        var subRepo = new SubscriptionRepository(context);
        var userRepo = new UserRepository(context);
        var limitService = new UsageLimitService(subRepo, context, new Mock<ILogger<UsageLimitService>>().Object);
        var subService = new SubscriptionService(subRepo, limitService, new Mock<ILogger<SubscriptionService>>().Object);

        return new PaymentService(
            paymentRepo,
            subRepo,
            userRepo,
            subService,
            new IPaymentProvider[] { _mockProvider },
            _config,
            new Mock<ILogger<PaymentService>>().Object
        );
    }

    private string ComputeMockSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test_mock_webhook_secret_12345"));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── 1. Create Checkout & Idempotency ────────────────────────────────────

    [Fact]
    public async Task Payment_CreateCheckout_CreatesPendingTransaction()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var user = SeedUser(context, 10);
        var paymentService = CreatePaymentService(context);

        var request = new CheckoutSessionRequest(2, "https://app/success", "https://app/cancel");
        var result = await paymentService.CreateCheckoutAsync(10, request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.SessionId);
        Assert.Contains("checkout", result.CheckoutUrl);

        var savedTx = await context.PaymentTransactions.FirstOrDefaultAsync(t => t.UserId == 10);
        Assert.NotNull(savedTx);
        Assert.Equal(PaymentStatus.Pending, savedTx.Status);
        Assert.Equal(199m, savedTx.Amount); // Price read from DB
        Assert.Equal("TRY", savedTx.Currency);
    }

    [Fact]
    public async Task Payment_DuplicateIdempotencyKey_DoesNotCreateSecondTransaction()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var user = SeedUser(context, 11);
        var paymentService = CreatePaymentService(context);

        var request = new CheckoutSessionRequest(2, "https://app/success", "https://app/cancel", "idem_key_12345");

        var firstResult = await paymentService.CreateCheckoutAsync(11, request);
        var secondResult = await paymentService.CreateCheckoutAsync(11, request);

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);
        Assert.Equal(firstResult.SessionId, secondResult.SessionId);

        var count = await context.PaymentTransactions.CountAsync(t => t.UserId == 11);
        Assert.Equal(1, count); // Only 1 transaction created
    }

    [Fact]
    public async Task Payment_DuplicatePremiumCheckout_IsRejected()
    {
        using var context = CreateContext();
        var (_, premium) = SeedPlans(context);
        var user = SeedUser(context, 12);

        // Give user active Premium subscription
        context.Subscriptions.Add(new Subscription
        {
            UserId = 12,
            PlanId = premium.Id,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var paymentService = CreatePaymentService(context);
        var request = new CheckoutSessionRequest(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => paymentService.CreateCheckoutAsync(12, request));
    }

    // ── 2. Webhooks & Subscription Activation ───────────────────────────────

    [Fact]
    public async Task Payment_SuccessWebhook_ActivatesSubscription()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var user = SeedUser(context, 13);
        var paymentService = CreatePaymentService(context);

        // Initiate checkout
        var checkout = await paymentService.CreateCheckoutAsync(13, new CheckoutSessionRequest(2));

        // Simulate incoming webhook from provider
        var payload = $"{{\"eventId\":\"{checkout.SessionId}\",\"eventType\":\"payment.success\"}}";
        var signature = ComputeMockSignature(payload);

        var webhookResult = await paymentService.ProcessWebhookAsync("Mock", payload, signature);
        Assert.True(webhookResult.Success);

        // Check transaction updated to Succeeded
        var tx = await context.PaymentTransactions.FirstAsync(t => t.ProviderTransactionId == checkout.SessionId);
        Assert.Equal(PaymentStatus.Succeeded, tx.Status);
        Assert.NotNull(tx.CompletedAt);

        // Check user subscription upgraded to PREMIUM
        var sub = await context.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.UserId == 13 && s.Status == SubscriptionStatus.Active);
        Assert.NotNull(sub);
        Assert.Equal("PREMIUM", sub.Plan.Name);
    }

    [Fact]
    public async Task Payment_DuplicateWebhook_IsIdempotent()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var user = SeedUser(context, 14);
        var paymentService = CreatePaymentService(context);

        var checkout = await paymentService.CreateCheckoutAsync(14, new CheckoutSessionRequest(2));
        var payload = $"{{\"eventId\":\"{checkout.SessionId}\",\"eventType\":\"payment.success\"}}";
        var signature = ComputeMockSignature(payload);

        var first = await paymentService.ProcessWebhookAsync("Mock", payload, signature);
        var second = await paymentService.ProcessWebhookAsync("Mock", payload, signature);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Contains("previously processed", second.Message);

        var eventCount = await context.PaymentWebhookEvents.CountAsync(w => w.EventId == checkout.SessionId);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task Payment_InvalidWebhookSignature_IsRejected()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var paymentService = CreatePaymentService(context);

        var payload = "{\"eventId\":\"evt_123\",\"eventType\":\"payment.success\"}";
        var invalidSignature = "invalid_signature_hex_value";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            paymentService.ProcessWebhookAsync("Mock", payload, invalidSignature));
    }

    [Fact]
    public async Task Payment_FailedWebhook_MarksTransactionFailed()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var user = SeedUser(context, 15);
        var paymentService = CreatePaymentService(context);

        var checkout = await paymentService.CreateCheckoutAsync(15, new CheckoutSessionRequest(2));
        var payload = $"{{\"eventId\":\"{checkout.SessionId}\",\"eventType\":\"payment.failed\",\"message\":\"Yetersiz bakiye\"}}";
        var signature = ComputeMockSignature(payload);

        var result = await paymentService.ProcessWebhookAsync("Mock", payload, signature);
        Assert.True(result.Success);

        var tx = await context.PaymentTransactions.FirstAsync(t => t.ProviderTransactionId == checkout.SessionId);
        Assert.Equal(PaymentStatus.Failed, tx.Status);
        Assert.NotNull(tx.FailedAt);
    }

    // ── 3. IDOR & Security Isolation ────────────────────────────────────────

    [Fact]
    public async Task Payment_UserCannotAccessAnotherUsersTransaction()
    {
        using var context = CreateContext();
        SeedPlans(context);
        var userA = SeedUser(context, 20, "usera@test.com");
        var userB = SeedUser(context, 21, "userb@test.com");
        var paymentService = CreatePaymentService(context);

        var checkoutA = await paymentService.CreateCheckoutAsync(20, new CheckoutSessionRequest(2));
        var txA = await context.PaymentTransactions.FirstAsync(t => t.UserId == 20);

        // User B tries to view User A's transaction
        var viewedByB = await paymentService.GetPaymentTransactionByIdAsync(txA.Id, 21);
        Assert.Null(viewedByB); // Blocked by IDOR protection
    }

    [Fact]
    public async Task Payment_History_IsUserScoped()
    {
        using var context = CreateContext();
        SeedPlans(context);
        SeedUser(context, 30, "u30@test.com");
        SeedUser(context, 31, "u31@test.com");
        var paymentService = CreatePaymentService(context);

        await paymentService.CreateCheckoutAsync(30, new CheckoutSessionRequest(2));
        await paymentService.CreateCheckoutAsync(30, new CheckoutSessionRequest(2, IdempotencyKey: "u30_2"));
        await paymentService.CreateCheckoutAsync(31, new CheckoutSessionRequest(2));

        var history30 = await paymentService.GetUserPaymentHistoryAsync(30, 1, 20);
        var history31 = await paymentService.GetUserPaymentHistoryAsync(31, 1, 20);

        Assert.Equal(2, history30.TotalCount);
        Assert.All(history30.Items, i => Assert.Equal(30, i.UserId));

        Assert.Equal(1, history31.TotalCount);
        Assert.All(history31.Items, i => Assert.Equal(31, i.UserId));
    }

    [Fact]
    public async Task Payment_Refund_UpdatesTransactionAndDowngradesSubscription()
    {
        using var context = CreateContext();
        SeedPlans(context);
        SeedUser(context, 40, "u40@test.com");
        var paymentService = CreatePaymentService(context);

        var checkout = await paymentService.CreateCheckoutAsync(40, new CheckoutSessionRequest(2));
        var payload = $"{{\"eventId\":\"{checkout.SessionId}\",\"eventType\":\"payment.success\"}}";
        await paymentService.ProcessWebhookAsync("Mock", payload, ComputeMockSignature(payload));

        var tx = await context.PaymentTransactions.FirstAsync(t => t.ProviderTransactionId == checkout.SessionId);

        // Trigger Refund
        var refundResult = await paymentService.RefundTransactionAsync(tx.Id, 40, reason: "Müşteri memnuniyeti");
        Assert.True(refundResult.Success);
        Assert.Equal(PaymentStatus.Refunded, refundResult.Status);

        var sub = await context.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.UserId == 40 && s.Status == SubscriptionStatus.Active);
        Assert.NotNull(sub);
        Assert.Equal("FREE", sub.Plan.Name); // Downgraded to FREE
    }

    [Fact]
    public void Payment_CardDataNeverPersisted()
    {
        var entityProps = typeof(PaymentTransaction).GetProperties().Select(p => p.Name.ToLower()).ToList();
        Assert.DoesNotContain("cardnumber", entityProps);
        Assert.DoesNotContain("pan", entityProps);
        Assert.DoesNotContain("cvv", entityProps);
        Assert.DoesNotContain("cvc", entityProps);
        Assert.DoesNotContain("expiry", entityProps);
    }
}
