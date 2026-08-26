namespace StockTracker.Domain.Entities;

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Cancelled = 3,
    Expired = 4
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public string BillingPeriod { get; set; } = "Monthly";

    // Plan Limits
    public int MaxActiveMonitors { get; set; } = 5;
    public int MaxTotalMonitors { get; set; } = 10;
    public int MinCheckIntervalMinutes { get; set; } = 60;
    public bool TelegramEnabled { get; set; } = true;
    public int MaxNotificationsPerDay { get; set; } = 20;
    public int MaxInspectRequestsPerDay { get; set; } = 20;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

public class Subscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Future Payment Provider Metadata
    public string? PaymentProvider { get; set; }
    public string? ExternalSubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsActiveSubscription => Status == SubscriptionStatus.Active || Status == SubscriptionStatus.Trial;

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}

public class DailyUsageRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string DateKey { get; set; } = string.Empty; // Format: "yyyy-MM-dd"
    public int InspectRequestsCount { get; set; } = 0;
    public int NotificationsCount { get; set; } = 0;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}
