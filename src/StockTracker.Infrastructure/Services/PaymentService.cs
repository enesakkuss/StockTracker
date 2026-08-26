using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Payments.Iyzico;
using StockTracker.Infrastructure.Payments.Mock;

namespace StockTracker.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentTransactionRepository _paymentRepo;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IUserRepository _userRepo;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly string _defaultProviderName;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentTransactionRepository paymentRepo,
        ISubscriptionRepository subscriptionRepo,
        IUserRepository userRepo,
        ISubscriptionService subscriptionService,
        IEnumerable<IPaymentProvider> providers,
        IConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _paymentRepo = paymentRepo;
        _subscriptionRepo = subscriptionRepo;
        _userRepo = userRepo;
        _subscriptionService = subscriptionService;
        _providers = providers;
        _defaultProviderName = configuration["Payment:Provider"] ?? "Mock";
        _logger = logger;
    }

    private IPaymentProvider GetProvider(string? name = null)
    {
        var target = string.IsNullOrWhiteSpace(name) ? _defaultProviderName : name.Trim();
        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, target, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            // Fallback to first available or Mock
            provider = _providers.FirstOrDefault(p => p.ProviderName == "Mock")
                ?? _providers.First();
        }
        return provider;
    }

    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(int userId, CheckoutSessionRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new KeyNotFoundException("Kullanıcı bulunamadı.");
        }

        var plan = await _subscriptionRepo.GetPlanByIdAsync(request.PlanId, cancellationToken);
        if (plan is null || !plan.IsActive)
        {
            throw new KeyNotFoundException("Seçilen abonelik planı bulunamadı veya aktif değil.");
        }

        // 1. Authoritative plan price & currency from DB
        var amount = plan.Price;
        var currency = plan.Currency;

        // 2. Existing subscription checks & duplicate purchase prevention
        var existingSub = await _subscriptionRepo.GetActiveSubscriptionByUserIdAsync(userId, cancellationToken);
        if (existingSub != null && existingSub.PlanId == plan.Id && existingSub.IsActiveSubscription)
        {
            throw new InvalidOperationException($"Zaten {plan.Name} planına ait aktif bir aboneliğiniz bulunmaktadır.");
        }

        // 3. Strong Idempotency check: if request was already initiated with same key
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingTx = await _paymentRepo.GetByIdempotencyKeyAsync(userId, request.IdempotencyKey.Trim(), cancellationToken);
            if (existingTx != null)
            {
                _logger.LogInformation("Idempotent checkout hit for user {UserId}, idempotencyKey: {Key}", userId, request.IdempotencyKey);
                var existingCheckoutUrl = $"https://checkout.stocktracker.local/pay/{existingTx.ProviderTransactionId}";
                return new PaymentCheckoutResult(
                    Success: true,
                    SessionId: existingTx.ProviderTransactionId,
                    CheckoutUrl: existingCheckoutUrl,
                    Provider: existingTx.Provider
                );
            }
        }

        var provider = GetProvider();
        var paymentType = (existingSub != null && existingSub.Plan?.Name == "FREE" && plan.Name == "PREMIUM")
            ? PaymentType.Upgrade
            : PaymentType.SubscriptionPurchase;

        // 4. Create pending transaction
        var transaction = new PaymentTransaction
        {
            UserId = userId,
            SubscriptionId = existingSub?.Id,
            Provider = provider.ProviderName,
            ProviderTransactionId = "pending_" + Guid.NewGuid().ToString("N"),
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            PaymentType = paymentType,
            IdempotencyKey = request.IdempotencyKey?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _paymentRepo.AddTransactionAsync(transaction, cancellationToken);

        // 5. Delegate to provider
        var checkoutResult = await provider.CreateCheckoutSessionAsync(
            user, plan, transaction, request.SuccessUrl, request.CancelUrl, cancellationToken);

        if (checkoutResult.Success && !string.IsNullOrWhiteSpace(checkoutResult.SessionId))
        {
            transaction.ProviderTransactionId = checkoutResult.SessionId;
            await _paymentRepo.UpdateTransactionAsync(transaction, cancellationToken);
        }

        return checkoutResult;
    }

    public async Task<WebhookResult> ProcessWebhookAsync(string providerName, string payload, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerName);

        // 1. Webhook signature verification
        var isValidSignature = await provider.VerifyWebhookSignatureAsync(payload, signatureHeader, cancellationToken);
        if (!isValidSignature)
        {
            _logger.LogWarning("Invalid webhook signature from provider: {Provider}", providerName);
            throw new UnauthorizedAccessException("Geçersiz webhook imzası.");
        }

        // 2. Parse webhook event
        var parsed = await provider.ParseWebhookPayloadAsync(payload, cancellationToken);
        if (!parsed.Success || string.IsNullOrWhiteSpace(parsed.EventId))
        {
            return new WebhookResult(false, parsed.EventId, parsed.EventType, "Ayrıştırma başarısız.");
        }

        // 3. Webhook idempotency: check if event was already processed
        var alreadyProcessed = await _paymentRepo.HasWebhookBeenProcessedAsync(provider.ProviderName, parsed.EventId, cancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Webhook event {EventId} from {Provider} already processed. Skipping duplicate.", parsed.EventId, provider.ProviderName);
            return new WebhookResult(true, parsed.EventId, parsed.EventType, "Event previously processed.");
        }

        // 4. Save webhook event record
        using var sha = SHA256.Create();
        var payloadHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        var webhookEntity = new PaymentWebhookEvent
        {
            Provider = provider.ProviderName,
            EventId = parsed.EventId,
            EventType = parsed.EventType,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
            PayloadHash = payloadHash,
            ProcessingStatus = "Processed"
        };
        await _paymentRepo.AddWebhookEventAsync(webhookEntity, cancellationToken);

        // 5. Execute state transition based on event type
        var transaction = await _paymentRepo.GetByProviderTransactionIdAsync(provider.ProviderName, parsed.EventId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning("Transaction not found for provider transaction ID: {Id}", parsed.EventId);
            return new WebhookResult(true, parsed.EventId, parsed.EventType, "Transaction record not found but event logged.");
        }

        var normalizedType = parsed.EventType.ToLowerInvariant();
        if (normalizedType.Contains("success") && !normalizedType.Contains("refund"))
        {
            transaction.Status = PaymentStatus.Succeeded;
            transaction.CompletedAt = DateTime.UtcNow;
            await _paymentRepo.UpdateTransactionAsync(transaction, cancellationToken);

            // Activate subscription
            await _subscriptionService.AssignPlanAsync(transaction.UserId, "PREMIUM", cancellationToken);
            _logger.LogInformation("Payment transaction {Id} succeeded. Premium subscription activated for user {UserId}", transaction.Id, transaction.UserId);
        }
        else if (normalizedType.Contains("failed"))
        {
            transaction.Status = PaymentStatus.Failed;
            transaction.FailedAt = DateTime.UtcNow;
            transaction.FailureReason = parsed.Message ?? "Ödeme işlemi başarısız oldu.";
            await _paymentRepo.UpdateTransactionAsync(transaction, cancellationToken);
            _logger.LogInformation("Payment transaction {Id} marked failed for user {UserId}", transaction.Id, transaction.UserId);
        }
        else if (normalizedType.Contains("refund"))
        {
            transaction.Status = PaymentStatus.Refunded;
            await _paymentRepo.UpdateTransactionAsync(transaction, cancellationToken);

            // Downgrade subscription to FREE
            await _subscriptionService.AssignPlanAsync(transaction.UserId, "FREE", cancellationToken);
            _logger.LogInformation("Payment transaction {Id} refunded. Subscription downgraded to FREE for user {UserId}", transaction.Id, transaction.UserId);
        }

        return new WebhookResult(true, parsed.EventId, parsed.EventType, "Processed successfully");
    }

    public async Task<PaymentTransactionDto?> GetPaymentTransactionByIdAsync(int transactionId, int userId, CancellationToken cancellationToken = default)
    {
        var tx = await _paymentRepo.GetByIdAsync(transactionId, cancellationToken);
        if (tx is null || tx.UserId != userId)
        {
            return null; // IDOR safe
        }

        return MapToDto(tx);
    }

    public async Task<PagedResponse<PaymentTransactionDto>> GetUserPaymentHistoryAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var paged = await _paymentRepo.GetPagedByUserIdAsync(userId, page, pageSize, cancellationToken);
        var dtos = paged.Items.Select(MapToDto).ToList();
        return new PagedResponse<PaymentTransactionDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<PaymentResult> RefundTransactionAsync(int transactionId, int? userId = null, decimal? amount = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        var tx = await _paymentRepo.GetByIdAsync(transactionId, cancellationToken);
        if (tx is null || (userId.HasValue && tx.UserId != userId.Value))
        {
            return new PaymentResult(false, string.Empty, PaymentStatus.Failed, "NOT_FOUND", "İşlem bulunamadı.");
        }

        var provider = GetProvider(tx.Provider);
        var refundResult = await provider.RefundAsync(tx, amount, reason, cancellationToken);

        if (refundResult.Success)
        {
            tx.Status = PaymentStatus.Refunded;
            await _paymentRepo.UpdateTransactionAsync(tx, cancellationToken);

            // Downgrade to FREE
            await _subscriptionService.AssignPlanAsync(tx.UserId, "FREE", cancellationToken);
            _logger.LogInformation("Transaction {TxId} refunded and user {UserId} downgraded to FREE", transactionId, tx.UserId);
        }

        return refundResult;
    }

    private static PaymentTransactionDto MapToDto(PaymentTransaction t)
    {
        return new PaymentTransactionDto(
            t.Id,
            t.UserId,
            t.SubscriptionId,
            t.Provider,
            t.ProviderTransactionId,
            t.Amount,
            t.Currency,
            t.Status.ToString(),
            t.PaymentType.ToString(),
            t.CreatedAt,
            t.CompletedAt,
            t.FailureReason
        );
    }
}
