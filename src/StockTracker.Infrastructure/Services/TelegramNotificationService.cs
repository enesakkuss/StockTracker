using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Infrastructure.Services;

public class TelegramNotificationService : ITelegramService, INotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<TelegramNotificationService> _logger;
    private readonly int _timeoutSeconds;

    public const string TestSuccessMessage = "🟢 StockTracker bağlantı testi başarılı!\n\nTelegram bildirimleri bu bot üzerinden gönderilebilir.";

    public TelegramNotificationService(
        IHttpClientFactory httpClientFactory,
        ISecretProtector secretProtector,
        IConfiguration configuration,
        ILogger<TelegramNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretProtector = secretProtector;
        _logger = logger;

        if (!int.TryParse(configuration["Telegram:TimeoutSeconds"], out _timeoutSeconds) || _timeoutSeconds < 5)
        {
            _timeoutSeconds = 15;
        }
    }

    public async Task<bool> NotifyStockAvailableAsync(
        StockAvailableNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (notification is null) return false;

        string plainToken;
        try
        {
            plainToken = _secretProtector.Unprotect(notification.ProtectedTelegramBotToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt Telegram Bot Token for monitor ID: {Id}", notification.MonitorId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(plainToken) || string.IsNullOrWhiteSpace(notification.TelegramChatId))
        {
            _logger.LogWarning("Cannot send notification: Missing bot token or ChatId for monitor ID: {Id}", notification.MonitorId);
            return false;
        }

        var messageText = BuildStockAlertMessage(notification);
        var replyMarkup = BuildInlineKeyboard(notification.ProductUrl);

        var client = _httpClientFactory.CreateClient("TelegramNotificationApi");
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

        // 1. Try sendPhoto if ImageUrl is available
        if (!string.IsNullOrWhiteSpace(notification.ImageUrl))
        {
            try
            {
                var sendPhotoUrl = $"https://api.telegram.org/bot{plainToken}/sendPhoto";
                var photoPayload = new
                {
                    chat_id = notification.TelegramChatId,
                    photo = notification.ImageUrl.Trim(),
                    caption = messageText,
                    parse_mode = "HTML",
                    reply_markup = replyMarkup
                };

                using var photoResponse = await client.PostAsJsonAsync(sendPhotoUrl, photoPayload, cancellationToken);
                var photoJson = await photoResponse.Content.ReadFromJsonAsync<TelegramApiResponse<JsonElement>>(cancellationToken: cancellationToken);

                if (photoResponse.IsSuccessStatusCode && photoJson is not null && photoJson.Ok)
                {
                    _logger.LogInformation(
                        "Stock arrival notification with photo sent successfully for monitor {Id} ({Variant}) to {ChatId}",
                        notification.MonitorId, notification.VariantName, MaskChatId(notification.TelegramChatId));
                    return true;
                }

                _logger.LogWarning(
                    "sendPhoto failed for monitor {Id} (Status: {Status}, Desc: {Desc}). Falling back to sendMessage.",
                    notification.MonitorId, (int)photoResponse.StatusCode, photoJson?.Description ?? "Unknown");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sendPhoto exception for monitor {Id}. Falling back to sendMessage.", notification.MonitorId);
            }
        }

        // 2. Fallback: sendMessage
        try
        {
            var sendMessageUrl = $"https://api.telegram.org/bot{plainToken}/sendMessage";
            var textPayload = new
            {
                chat_id = notification.TelegramChatId,
                text = messageText,
                parse_mode = "HTML",
                reply_markup = replyMarkup
            };

            using var textResponse = await client.PostAsJsonAsync(sendMessageUrl, textPayload, cancellationToken);
            var textJson = await textResponse.Content.ReadFromJsonAsync<TelegramApiResponse<JsonElement>>(cancellationToken: cancellationToken);

            if (textResponse.IsSuccessStatusCode && textJson is not null && textJson.Ok)
            {
                _logger.LogInformation(
                    "Stock arrival text notification sent successfully for monitor {Id} ({Variant}) to {ChatId}",
                    notification.MonitorId, notification.VariantName, MaskChatId(notification.TelegramChatId));
                return true;
            }

            _logger.LogWarning(
                "sendMessage failed for monitor {Id} (Status: {Status}, Desc: {Desc})",
                notification.MonitorId, (int)textResponse.StatusCode, textJson?.Description ?? "Unknown");
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram sendMessage timed out after {Timeout}s for monitor ID: {Id}", _timeoutSeconds, notification.MonitorId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending Telegram text notification for monitor ID: {Id}", notification.MonitorId);
            return false;
        }
    }

    public async Task<TelegramTestResponse> TestConnectionAsync(
        string botToken,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return new TelegramTestResponse(false, "Bot Token boş olamaz.");

        if (string.IsNullOrWhiteSpace(chatId))
            return new TelegramTestResponse(false, "Chat ID boş olamaz.");

        botToken = botToken.Trim();
        chatId = chatId.Trim();

        var client = _httpClientFactory.CreateClient("TelegramTestApi");
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

        try
        {
            // 1. getMe
            var getMeUrl = $"https://api.telegram.org/bot{botToken}/getMe";
            using var getMeResponse = await client.GetAsync(getMeUrl, cancellationToken);

            if (!getMeResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram getMe validation failed with status code: {StatusCode}", (int)getMeResponse.StatusCode);
                return new TelegramTestResponse(false, "Telegram bağlantısı başarısız. Bot Token geçersiz.");
            }

            var getMeJson = await getMeResponse.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramUser>>(cancellationToken: cancellationToken);
            if (getMeJson is null || !getMeJson.Ok || getMeJson.Result is null)
            {
                return new TelegramTestResponse(false, "Telegram bağlantısı başarısız. Bot Token doğrulanamadı.");
            }

            // 2. sendMessage test
            var sendMessageUrl = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = TestSuccessMessage
            };

            using var sendResponse = await client.PostAsJsonAsync(sendMessageUrl, payload, cancellationToken);
            var sendJson = await sendResponse.Content.ReadFromJsonAsync<TelegramApiResponse<JsonElement>>(cancellationToken: cancellationToken);

            if (sendResponse.IsSuccessStatusCode && sendJson is not null && sendJson.Ok)
            {
                _logger.LogInformation("Telegram test message sent successfully to ChatId: {ChatId}", MaskChatId(chatId));
                return new TelegramTestResponse(true, "Telegram bağlantısı başarılı.");
            }

            var desc = sendJson?.Description?.ToLowerInvariant() ?? "";
            if (desc.Contains("chat not found") || desc.Contains("chat_id is empty"))
            {
                return new TelegramTestResponse(false, "Telegram bağlantısı başarısız. Chat ID bulunamadı. Lütfen önce Telegram'da botunuza bir mesaj gönderin.");
            }
            if (desc.Contains("bot was blocked"))
            {
                return new TelegramTestResponse(false, "Telegram bağlantısı başarısız. Bot kullanıcı tarafından engellenmiş.");
            }

            _logger.LogWarning("Telegram sendMessage failed with description: {Description}", sendJson?.Description ?? "Unknown");
            return new TelegramTestResponse(false, "Telegram bağlantısı başarısız. Chat ID'yi kontrol edin ve bota mesaj attığınızdan emin olun.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram API call timed out.");
            return new TelegramTestResponse(false, "Telegram sunucusuna bağlanırken zaman aşımına uğrandı. Lütfen tekrar deneyin.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Telegram API HTTP connection failure.");
            return new TelegramTestResponse(false, "Telegram sunucusuna bağlanılamadı. İnternet bağlantınızı kontrol edin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Telegram connection test.");
            return new TelegramTestResponse(false, "Telegram bağlantısı sırasında beklenmeyen bir hata oluştu.");
        }
    }

    private static string BuildStockAlertMessage(StockAvailableNotification notification)
    {
        var storeEncoded = WebUtility.HtmlEncode(notification.Store.ToUpperInvariant());
        var nameEncoded = WebUtility.HtmlEncode(notification.ProductName);
        var variantEncoded = WebUtility.HtmlEncode(notification.VariantName);

        return $"🟢 <b>STOK GELDİ!</b>\n\n" +
               $"<b>Mağaza:</b> {storeEncoded}\n\n" +
               $"<b>Ürün:</b>\n{nameEncoded}\n\n" +
               $"<b>Beden:</b>\n{variantEncoded}";
    }

    private static object BuildInlineKeyboard(string productUrl)
    {
        return new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new
                    {
                        text = "🛒 ÜRÜNE GİT",
                        url = productUrl
                    }
                }
            }
        };
    }

    private static string MaskChatId(string chatId)
    {
        if (chatId.Length <= 4) return "****";
        return chatId[..2] + new string('*', chatId.Length - 4) + chatId[^2..];
    }

    private class TelegramApiResponse<T>
    {
        public bool Ok { get; set; }
        public T? Result { get; set; }
        public string? Description { get; set; }
        public int? ErrorCode { get; set; }
    }

    private class TelegramUser
    {
        public long Id { get; set; }
        public bool IsBot { get; set; }
        public string? FirstName { get; set; }
        public string? Username { get; set; }
    }
}
