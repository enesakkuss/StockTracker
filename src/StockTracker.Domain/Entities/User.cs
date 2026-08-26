namespace StockTracker.Domain.Entities;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Encrypted/protected default Telegram Bot Token for user.
    /// </summary>
    public string? ProtectedTelegramBotToken { get; set; }

    /// <summary>
    /// Default Telegram Chat ID for user.
    /// </summary>
    public string? TelegramChatId { get; set; }

    // User Preferences
    public bool TelegramNotificationsEnabled { get; set; } = true;

    public string NotificationLanguage { get; set; } = "tr";

    public int DefaultCheckIntervalMinutes { get; set; } = 10;

    public string Timezone { get; set; } = "Europe/Istanbul";

    public string? PushNotificationToken { get; set; }

    // Navigation properties
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<StockMonitor> Monitors { get; set; } = new List<StockMonitor>();

    public ICollection<StockNotificationHistory> NotificationHistories { get; set; } = new List<StockNotificationHistory>();

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public ICollection<DailyUsageRecord> DailyUsageRecords { get; set; } = new List<DailyUsageRecord>();

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}
