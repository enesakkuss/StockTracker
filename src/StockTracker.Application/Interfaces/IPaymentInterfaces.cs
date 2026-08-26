using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

public interface IPaymentTransactionRepository
{
    Task<PaymentTransaction> AddTransactionAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByProviderTransactionIdAsync(string provider, string providerTransactionId, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByIdempotencyKeyAsync(int userId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentTransaction>> GetPagedByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task UpdateTransactionAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default);

    Task<bool> HasWebhookBeenProcessedAsync(string provider, string eventId, CancellationToken cancellationToken = default);
    Task AddWebhookEventAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken = default);
}

public interface IPaymentProvider
{
    string ProviderName { get; }
    Task<PaymentCheckoutResult> CreateCheckoutSessionAsync(User user, SubscriptionPlan plan, PaymentTransaction transaction, string? successUrl, string? cancelUrl, CancellationToken cancellationToken = default);
    Task<PaymentResult> GetPaymentStatusAsync(string providerTransactionId, CancellationToken cancellationToken = default);
    Task<PaymentResult> RefundAsync(PaymentTransaction transaction, decimal? amount = null, string? reason = null, CancellationToken cancellationToken = default);
    Task<PaymentResult> CancelPaymentAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default);
    Task<bool> VerifyWebhookSignatureAsync(string payload, string? signatureHeader, CancellationToken cancellationToken = default);
    Task<WebhookResult> ParseWebhookPayloadAsync(string payload, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<PaymentCheckoutResult> CreateCheckoutAsync(int userId, CheckoutSessionRequest request, CancellationToken cancellationToken = default);
    Task<PaymentTransactionDto?> GetPaymentTransactionByIdAsync(int transactionId, int userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentTransactionDto>> GetUserPaymentHistoryAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<WebhookResult> ProcessWebhookAsync(string provider, string payload, string? signatureHeader, CancellationToken cancellationToken = default);
    Task<PaymentResult> RefundTransactionAsync(int transactionId, int? userId = null, decimal? amount = null, string? reason = null, CancellationToken cancellationToken = default);
}
