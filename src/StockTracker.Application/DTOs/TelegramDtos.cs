namespace StockTracker.Application.DTOs;

public record TelegramTestRequest(
    string BotToken,
    string ChatId
);

public record TelegramTestResponse(
    bool Success,
    string Message
);
