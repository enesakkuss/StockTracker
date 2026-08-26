namespace StockTracker.Application.DTOs;

public record CreateMonitorRequest(
    string ProductUrl,
    string Store,
    string ProductName,
    string? ImageUrl,
    IReadOnlyList<string> SelectedVariants,
    string TelegramBotToken,
    string TelegramChatId,
    int CheckIntervalMinutes = 10
);

public record StockMonitorDto(
    int Id,
    string ProductUrl,
    string Store,
    string ProductName,
    string? ImageUrl,
    IReadOnlyList<string> SelectedVariants,
    int CheckIntervalMinutes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastCheckedAt = null,
    DateTime? NextCheckAt = null,
    string? LastCheckStatus = null,
    string? LastCheckError = null,
    DateTime? LastNotifiedAt = null,
    string? LastNotifiedVariant = null
);

public record StockChange(
    int MonitorId,
    string ProductName,
    string Store,
    string ProductUrl,
    string VariantName,
    bool? PreviousAvailability,
    bool CurrentAvailability,
    DateTime ChangedAt,
    bool IsInitialCheck
);

public record StockAvailableNotification(
    int MonitorId,
    string Store,
    string ProductName,
    string ProductUrl,
    string? ImageUrl,
    string VariantName,
    string ProtectedTelegramBotToken,
    string TelegramChatId
);

public record ManualCheckResponse(
    int MonitorId,
    string ProductName,
    string Store,
    string Status,
    IReadOnlyList<StockChange> Changes,
    string? Error = null,
    int NotificationsSent = 0
);
