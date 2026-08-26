namespace StockTracker.Application.DTOs;

public record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName
);

public record LoginRequest(
    string Email,
    string Password
);

public record RefreshTokenRequest(
    string? AccessToken,
    string RefreshToken
);

public record LogoutRequest(
    string? RefreshToken
);

public record AuthResponse(
    string Token,
    string? RefreshToken,
    DateTime Expiration,
    UserDto User
);

public record UserDto(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool HasTelegramConfigured = false
);

public record UserPreferencesDto(
    bool TelegramNotificationsEnabled,
    string NotificationLanguage,
    int DefaultCheckIntervalMinutes,
    string Timezone,
    string? PushNotificationToken
);

public record UserProfileDto(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    UserPreferencesDto Preferences,
    bool HasTelegramConfigured = false
);

public record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    UserPreferencesDto? Preferences
);

public record UserTelegramSettingsDto(
    bool IsConfigured,
    string? MaskedBotToken,
    string? ChatId
);

public record UpdateTelegramSettingsRequest(
    string BotToken,
    string ChatId
);

public record DashboardSummaryDto(
    int TotalMonitors,
    int ActiveMonitors,
    int PausedMonitors,
    int AvailableItems,
    int NotificationsToday,
    DateTime? LastNotificationAt
);
