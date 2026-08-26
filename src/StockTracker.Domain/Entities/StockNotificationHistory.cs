namespace StockTracker.Domain.Entities;

public class StockNotificationHistory
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    public int StockMonitorId { get; set; }

    public string VariantName { get; set; } = string.Empty;

    public bool PreviousAvailability { get; set; }

    public bool CurrentAvailability { get; set; }

    public DateTime StockChangeAt { get; set; } = DateTime.UtcNow;

    public DateTime NotificationSentAt { get; set; } = DateTime.UtcNow;

    public bool Success { get; set; }

    public string? Error { get; set; }

    public StockMonitor StockMonitor { get; set; } = null!;
}
