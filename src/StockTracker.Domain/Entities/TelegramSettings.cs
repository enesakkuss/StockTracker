namespace StockTracker.Domain.Entities;

public class TelegramSettings
{
    public int Id { get; set; }

    /// <summary>
    /// Telegram Bot Token - stored encrypted or via user secrets, never plain text in source.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Target Telegram Chat ID to send notifications to.
    /// </summary>
    public string ChatId { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
