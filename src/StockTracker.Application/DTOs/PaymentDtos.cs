using StockTracker.Domain.Entities;

namespace StockTracker.Application.DTOs;

public record CheckoutSessionRequest(
    int PlanId,
    string? SuccessUrl = null,
    string? CancelUrl = null,
    string? IdempotencyKey = null
);

public record PaymentCheckoutResult(
    bool Success,
    string SessionId,
    string CheckoutUrl,
    string Provider,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

public record PaymentResult(
    bool Success,
    string TransactionId,
    PaymentStatus Status,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

public record WebhookResult(
    bool Success,
    string EventId,
    string EventType,
    string? Message = null
);

public record PaymentTransactionDto(
    int Id,
    int UserId,
    int? SubscriptionId,
    string Provider,
    string ProviderTransactionId,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentType,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? FailureReason
);

public class PaymentHistoryQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
