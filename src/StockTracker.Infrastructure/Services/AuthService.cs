using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ILogger<AuthService> _logger;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request), "Kayıt bilgileri boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !EmailRegex.IsMatch(request.Email.Trim()))
        {
            throw new ArgumentException("Geçerli bir e-posta adresi giriniz.", nameof(request.Email));
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Trim().Length < 6)
        {
            throw new ArgumentException("Şifre en az 6 karakter uzunluğunda olmalıdır.", nameof(request.Password));
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw new ArgumentException("Ad alanı boş bırakılamaz.", nameof(request.FirstName));
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new ArgumentException("Soyad alanı boş bırakılamaz.", nameof(request.LastName));
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            throw new InvalidOperationException("Bu e-posta adresi ile kayıtlı bir kullanıcı zaten mevcut.");
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password.Trim());

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createdUser = await _userRepository.AddAsync(user, cancellationToken);
        _logger.LogInformation("New user registered successfully with ID: {UserId}, Email: {Email}", createdUser.Id, createdUser.Email);

        var (token, expiration) = _jwtTokenGenerator.GenerateToken(createdUser);

        // Generate & store Refresh Token
        var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var hashedRefreshToken = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken);
        var refreshTokenEntity = new RefreshToken
        {
            UserId = createdUser.Id,
            TokenHash = hashedRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };
        await _userRepository.AddRefreshTokenAsync(refreshTokenEntity, cancellationToken);

        var userDto = MapToDto(createdUser);
        return new AuthResponse(token, rawRefreshToken, expiration, userDto);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new UnauthorizedAccessException("E-posta veya şifre hatalı.");
        }

        var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("Login failed for non-existent email: {Email}", request.Email);
            throw new UnauthorizedAccessException("E-posta veya şifre hatalı.");
        }

        if (!_passwordHasher.VerifyPassword(request.Password.Trim(), user.PasswordHash))
        {
            _logger.LogWarning("Login failed: invalid password for user ID: {UserId}", user.Id);
            throw new UnauthorizedAccessException("E-posta veya şifre hatalı.");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login rejected: user account {UserId} is inactive", user.Id);
            throw new UnauthorizedAccessException("Kullanıcı hesabı devre dışı bırakılmıştır.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        _logger.LogInformation("User {UserId} logged in successfully", user.Id);

        var (token, expiration) = _jwtTokenGenerator.GenerateToken(user);

        // Generate & store Refresh Token
        var rawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var hashedRefreshToken = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken);
        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hashedRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };
        await _userRepository.AddRefreshTokenAsync(refreshTokenEntity, cancellationToken);

        var userDto = MapToDto(user);
        return new AuthResponse(token, rawRefreshToken, expiration, userDto);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string rawRefreshToken, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw new UnauthorizedAccessException("Refresh token boş olamaz.");
        }

        var tokenHash = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken.Trim());
        var storedToken = await _userRepository.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);

        if (storedToken is null)
        {
            _logger.LogWarning("Refresh token not found");
            throw new UnauthorizedAccessException("Geçersiz refresh token.");
        }

        // Detect refresh token reuse on already revoked token (potential breach -> revoke all user sessions)
        if (storedToken.IsRevoked)
        {
            _logger.LogWarning("Compromised refresh token reuse attempt for user {UserId}! Revoking all sessions.", storedToken.UserId);
            await _userRepository.RevokeAllUserRefreshTokensAsync(storedToken.UserId, ipAddress, cancellationToken);
            throw new UnauthorizedAccessException("Bu oturum daha önce sonlandırılmış. Lütfen tekrar giriş yapın.");
        }

        if (storedToken.IsExpired)
        {
            _logger.LogWarning("Refresh token expired for user {UserId}", storedToken.UserId);
            throw new UnauthorizedAccessException("Oturum süresi dolmuş. Lütfen tekrar giriş yapın.");
        }

        var user = storedToken.User ?? await _userRepository.GetByIdAsync(storedToken.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Kullanıcı hesabı aktif değil.");
        }

        // Generate new Access Token
        var (newAccessToken, expiration) = _jwtTokenGenerator.GenerateToken(user);

        // Generate new Refresh Token (Rotation)
        var newRawRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var newHashedRefreshToken = _jwtTokenGenerator.HashRefreshToken(newRawRefreshToken);

        var now = DateTime.UtcNow;
        storedToken.RevokedAt = now;
        storedToken.RevokedByIp = ipAddress;
        storedToken.ReplacedByTokenHash = newHashedRefreshToken;
        await _userRepository.UpdateRefreshTokenAsync(storedToken, cancellationToken);

        var newRefreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHashedRefreshToken,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now,
            CreatedByIp = ipAddress
        };
        await _userRepository.AddRefreshTokenAsync(newRefreshTokenEntity, cancellationToken);

        _logger.LogInformation("Refresh token rotated successfully for user {UserId}", user.Id);

        var userDto = MapToDto(user);
        return new AuthResponse(newAccessToken, newRawRefreshToken, expiration, userDto);
    }

    public async Task<bool> LogoutAsync(string? rawRefreshToken, int? userId = null, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = _jwtTokenGenerator.HashRefreshToken(rawRefreshToken.Trim());
            var storedToken = await _userRepository.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);
            if (storedToken != null && storedToken.IsActive)
            {
                storedToken.RevokedAt = DateTime.UtcNow;
                storedToken.RevokedByIp = ipAddress;
                await _userRepository.UpdateRefreshTokenAsync(storedToken, cancellationToken);
                _logger.LogInformation("Refresh token revoked on logout for user {UserId}", storedToken.UserId);
                return true;
            }
        }

        if (userId.HasValue)
        {
            await _userRepository.RevokeAllUserRefreshTokensAsync(userId.Value, ipAddress, cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> RevokeAllSessionsAsync(int userId, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        await _userRepository.RevokeAllUserRefreshTokensAsync(userId, ipAddress, cancellationToken);
        _logger.LogInformation("All active sessions revoked for user {UserId}", userId);
        return true;
    }

    public async Task<UserDto?> GetCurrentUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : MapToDto(user);
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return null;

        var preferences = new UserPreferencesDto(
            user.TelegramNotificationsEnabled,
            user.NotificationLanguage,
            user.DefaultCheckIntervalMinutes,
            user.Timezone,
            user.PushNotificationToken
        );

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            preferences,
            !string.IsNullOrWhiteSpace(user.ProtectedTelegramBotToken) && !string.IsNullOrWhiteSpace(user.TelegramChatId)
        );
    }

    public async Task<UserProfileDto> UpdateUserProfileAsync(int userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new KeyNotFoundException("Kullanıcı bulunamadı.");
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            user.FirstName = request.FirstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.LastName))
        {
            user.LastName = request.LastName.Trim();
        }

        if (request.Preferences != null)
        {
            user.TelegramNotificationsEnabled = request.Preferences.TelegramNotificationsEnabled;
            if (!string.IsNullOrWhiteSpace(request.Preferences.NotificationLanguage))
                user.NotificationLanguage = request.Preferences.NotificationLanguage.Trim();
            if (request.Preferences.DefaultCheckIntervalMinutes >= 1)
                user.DefaultCheckIntervalMinutes = request.Preferences.DefaultCheckIntervalMinutes;
            if (!string.IsNullOrWhiteSpace(request.Preferences.Timezone))
                user.Timezone = request.Preferences.Timezone.Trim();
            if (request.Preferences.PushNotificationToken != null)
                user.PushNotificationToken = request.Preferences.PushNotificationToken.Trim();
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("Profile updated for user {UserId}", userId);

        var updatedPreferences = new UserPreferencesDto(
            user.TelegramNotificationsEnabled,
            user.NotificationLanguage,
            user.DefaultCheckIntervalMinutes,
            user.Timezone,
            user.PushNotificationToken
        );

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            updatedPreferences,
            !string.IsNullOrWhiteSpace(user.ProtectedTelegramBotToken) && !string.IsNullOrWhiteSpace(user.TelegramChatId)
        );
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            !string.IsNullOrWhiteSpace(user.ProtectedTelegramBotToken) && !string.IsNullOrWhiteSpace(user.TelegramChatId)
        );
    }
}
