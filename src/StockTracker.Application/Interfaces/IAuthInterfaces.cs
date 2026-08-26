using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public interface IJwtTokenGenerator
{
    (string Token, DateTime Expiration) GenerateToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string token);
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        => RegisterAsync(request, null, cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        => LoginAsync(request, null, cancellationToken);

    Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<bool> LogoutAsync(string? refreshToken, int? userId = null, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<bool> RevokeAllSessionsAsync(int userId, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<UserDto?> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateUserProfileAsync(int userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}

public interface IUserTelegramService
{
    Task<UserTelegramSettingsDto> GetTelegramSettingsAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserTelegramSettingsDto> UpdateTelegramSettingsAsync(int userId, UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteTelegramSettingsAsync(int userId, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(int userId, CancellationToken cancellationToken = default);
}
