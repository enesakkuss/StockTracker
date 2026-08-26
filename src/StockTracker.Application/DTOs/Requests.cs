namespace StockTracker.Application.DTOs;

public record FetchProductRequest(string Url);

public record StartMonitorRequest(
    int ProductId,
    IReadOnlyList<int> VariantIds,
    int CheckIntervalSeconds = 60
);

public record TelegramSettingsDto(
    string BotToken,
    string ChatId
);
