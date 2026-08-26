using StockTracker.Application.DTOs;

namespace StockTracker.Application.Interfaces;

/// <summary>
/// Service abstraction for sending stock alerts (e.g., via Telegram).
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a rich stock arrival alert to the user's notification channel.
    /// Handles image sending with text fallback, safe formatting, and secret decryption.
    /// </summary>
    Task<bool> NotifyStockAvailableAsync(StockAvailableNotification notification, CancellationToken cancellationToken = default);
}
