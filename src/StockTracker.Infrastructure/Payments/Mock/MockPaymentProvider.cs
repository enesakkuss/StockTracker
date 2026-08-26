using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Payments.Mock;

public class MockPaymentProvider : IPaymentProvider
{
    public string ProviderName => "Mock";

    private readonly string _webhookSecret;
    private readonly ILogger<MockPaymentProvider> _logger;

    public MockPaymentProvider(IConfiguration configuration, ILogger<MockPaymentProvider> logger)
    {
        _webhookSecret = configuration["Payment:Mock:WebhookSecret"] ?? "mock_webhook_secret_key_2026";
        _logger = logger;
    }

    public Task<PaymentCheckoutResult> CreateCheckoutSessionAsync(
        User user,
        SubscriptionPlan plan,
        PaymentTransaction transaction,
        string? successUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var sessionId = "mock_sess_" + Guid.NewGuid().ToString("N");
        var checkoutUrl = $"https://checkout.stocktracker.local/pay/{sessionId}";

        _logger.LogInformation("Mock checkout session created for User {UserId}, Plan {PlanName}, Session: {SessionId}",
            user.Id, plan.Name, sessionId);

        return Task.FromResult(new PaymentCheckoutResult(
            Success: true,
            SessionId: sessionId,
            CheckoutUrl: checkoutUrl,
            Provider: ProviderName
        ));
    }

    public Task<PaymentResult> GetPaymentStatusAsync(string providerTransactionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: providerTransactionId,
            Status: PaymentStatus.Succeeded
        ));
    }

    public Task<PaymentResult> RefundAsync(PaymentTransaction transaction, decimal? amount = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock refund processed for transaction {Id}, amount: {Amount}", transaction.Id, amount ?? transaction.Amount);
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: transaction.ProviderTransactionId,
            Status: PaymentStatus.Refunded
        ));
    }

    public Task<PaymentResult> CancelPaymentAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock payment cancelled for transaction {Id}", transaction.Id);
        return Task.FromResult(new PaymentResult(
            Success: true,
            TransactionId: transaction.ProviderTransactionId,
            Status: PaymentStatus.Cancelled
        ));
    }

    public Task<bool> VerifyWebhookSignatureAsync(string payload, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return Task.FromResult(false);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

        var isValid = string.Equals(signatureHeader.Trim().ToLowerInvariant(), computedSignature, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(isValid);
    }

    public Task<WebhookResult> ParseWebhookPayloadAsync(string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var eventId = root.TryGetProperty("eventId", out var eid) ? eid.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
            var eventType = root.TryGetProperty("eventType", out var et) ? et.GetString() ?? "payment.success" : "payment.success";

            return Task.FromResult(new WebhookResult(
                Success: true,
                EventId: eventId,
                EventType: eventType,
                Message: "Mock webhook parsed successfully"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse mock webhook payload");
            return Task.FromResult(new WebhookResult(
                Success: false,
                EventId: string.Empty,
                EventType: string.Empty,
                Message: "Invalid JSON format"
            ));
        }
    }
}
