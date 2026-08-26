namespace StockTracker.Domain.Entities;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    Refunded = 4
}

public enum PaymentType
{
    SubscriptionPurchase = 0,
    SubscriptionRenewal = 1,
    Upgrade = 2,
    Downgrade = 3,
    Refund = 4
}

public class PaymentTransaction
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public string Provider { get; set; } = "Mock";

    /// <summary>
    /// Unique transaction or session ID returned from the payment gateway.
    /// </summary>
    public string ProviderTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Internal / external reference token, basket ID or conversation ID.
    /// </summary>
    public string? ProviderReference { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "TRY";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public PaymentType PaymentType { get; set; } = PaymentType.SubscriptionPurchase;

    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public DateTime? FailedAt { get; set; }

    public string? FailureReason { get; set; }
}

public class PaymentWebhookEvent
{
    public int Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public string PayloadHash { get; set; } = string.Empty;

    public string ProcessingStatus { get; set; } = "Processed"; // Processed, Failed, Ignored

    public string? ErrorMessage { get; set; }
}
