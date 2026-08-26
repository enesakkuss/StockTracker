using StockTracker.Application.DTOs;

namespace StockTracker.Application.Interfaces;

public interface ITelegramService
{
    /// <summary>
    /// Validates the provided bot token via getMe and sends a test message to the specified chatId.
    /// Never logs or leaks the bot token.
    /// </summary>
    Task<TelegramTestResponse> TestConnectionAsync(string botToken, string chatId, CancellationToken cancellationToken = default);
}
