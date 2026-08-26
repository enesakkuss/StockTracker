namespace StockTracker.Domain.Entities;

public class StockMonitorVariantState
{
    public int Id { get; set; }

    public int StockMonitorId { get; set; }

    public string VariantName { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastChangedAt { get; set; }

    public StockMonitor StockMonitor { get; set; } = null!;
}
