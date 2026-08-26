namespace StockTracker.Domain.Entities;

public class StockMonitor
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public string ProductUrl { get; set; } = string.Empty;

    public string Store { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public List<string> SelectedVariants { get; set; } = new();

    /// <summary>
    /// Encrypted/protected Telegram Bot Token — never stored or returned as plain text.
    /// </summary>
    public string ProtectedTelegramBotToken { get; set; } = string.Empty;

    public string TelegramChatId { get; set; } = string.Empty;

    public int CheckIntervalMinutes { get; set; } = 10;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    /// <summary>
    /// Next scheduled check time. Defaults to UtcNow when created so the worker checks immediately.
    /// </summary>
    public DateTime NextCheckAt { get; set; } = DateTime.UtcNow;

    public string? LastCheckStatus { get; set; }

    public string? LastCheckError { get; set; }

    public DateTime? LastNotifiedAt { get; set; }

    public string? LastNotifiedVariant { get; set; }

    public ICollection<StockMonitorVariantState> VariantStates { get; set; } = new List<StockMonitorVariantState>();

    public ICollection<StockNotificationHistory> NotificationHistories { get; set; } = new List<StockNotificationHistory>();
}
