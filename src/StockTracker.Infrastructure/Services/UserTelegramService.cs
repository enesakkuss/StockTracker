using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Infrastructure.Services;

public class UserTelegramService : IUserTelegramService
{
    private readonly IUserRepository _userRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<UserTelegramService> _logger;

    public UserTelegramService(
        IUserRepository userRepository,
        ISecretProtector secretProtector,
        ILogger<UserTelegramService> logger)
    {
        _userRepository = userRepository;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<UserTelegramSettingsDto> GetTelegramSettingsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.ProtectedTelegramBotToken) || string.IsNullOrWhiteSpace(user.TelegramChatId))
        {
            return new UserTelegramSettingsDto(false, null, null);
        }

        string maskedToken = "******";
        try
        {
            var plainToken = _secretProtector.Unprotect(user.ProtectedTelegramBotToken);
            maskedToken = MaskBotToken(plainToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unprotect Telegram token for masking for user: {UserId}", userId);
        }

        return new UserTelegramSettingsDto(true, maskedToken, user.TelegramChatId);
    }

    public async Task<UserTelegramSettingsDto> UpdateTelegramSettingsAsync(int userId, UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request), "Telegram ayarları boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(request.BotToken))
        {
            throw new ArgumentException("Telegram Bot Token boş olamaz.", nameof(request.BotToken));
        }

        if (string.IsNullOrWhiteSpace(request.ChatId))
        {
            throw new ArgumentException("Telegram Chat ID boş olamaz.", nameof(request.ChatId));
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new KeyNotFoundException($"Kullanıcı bulunamadı (ID: {userId}).");
        }

        var protectedToken = _secretProtector.Protect(request.BotToken.Trim());
        user.ProtectedTelegramBotToken = protectedToken;
        user.TelegramChatId = request.ChatId.Trim();

        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("Updated Telegram settings for user ID: {UserId}", userId);

        return new UserTelegramSettingsDto(true, MaskBotToken(request.BotToken.Trim()), user.TelegramChatId);
    }

    public async Task<bool> DeleteTelegramSettingsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return false;

        user.ProtectedTelegramBotToken = null;
        user.TelegramChatId = null;

        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("Deleted Telegram settings for user ID: {UserId}", userId);

        return true;
    }

    private static string MaskBotToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "******";
        if (token.Length <= 8) return "******";

        var prefix = token.Substring(0, 4);
        var suffix = token.Substring(token.Length - 4);
        return $"{prefix}••••••{suffix}";
    }
}
