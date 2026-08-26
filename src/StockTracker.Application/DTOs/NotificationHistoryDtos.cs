namespace StockTracker.Application.DTOs;

public record NotificationHistoryDto(
    int Id,
    int MonitorId,
    string Store,
    string ProductName,
    string? ProductImageUrl,
    string VariantName,
    bool PreviousAvailability,
    bool CurrentAvailability,
    DateTime NotificationSentAt,
    bool Success,
    string? Error
);

public class NotificationQueryParams
{
    public int? MonitorId { get; set; }
    public string? Store { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record UpdateMonitorRequest(
    List<string>? SelectedVariants,
    int? CheckIntervalMinutes,
    string? TelegramChatId
);
