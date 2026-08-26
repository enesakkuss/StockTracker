using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class UserExperienceAndContractHardeningTests
{
    private readonly IConfiguration _config;
    private readonly PasswordHasher _passwordHasher = new();
    private readonly JwtTokenGenerator _jwtTokenGenerator;

    public UserExperienceAndContractHardeningTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:SecretKey", "Test_Super_Secret_Key_For_Unit_Testing_32_Bytes_Long_2026!" },
                { "Jwt:Issuer", "StockTrackerTest" },
                { "Jwt:Audience", "StockTrackerTestAudience" },
                { "Jwt:ExpirationMinutes", "15" }
            })
            .Build();

        _jwtTokenGenerator = new JwtTokenGenerator(_config);
    }

    // ── 1. Refresh Token & Session Management Tests ───────────────────────────

    [Fact]
    public async Task AuthService_Login_GeneratesAccessTokenAndRefreshToken()
    {
        var passwordHash = _passwordHasher.HashPassword("Pass123456!");
        var user = new User { Id = 1, Email = "user@test.com", PasswordHash = passwordHash, FirstName = "A", LastName = "B", IsActive = true };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByEmailAsync("user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        RefreshToken? savedRefreshToken = null;
        userRepoMock.Setup(r => r.AddRefreshTokenAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((r, _) => savedRefreshToken = r)
            .Returns(Task.CompletedTask);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var response = await authService.LoginAsync(new LoginRequest("user@test.com", "Pass123456!"));

        Assert.NotNull(response.Token);
        Assert.NotNull(response.RefreshToken);
        Assert.NotNull(savedRefreshToken);
        Assert.Equal(user.Id, savedRefreshToken.UserId);
        Assert.False(savedRefreshToken.IsRevoked);
        Assert.False(savedRefreshToken.IsExpired);
    }

    [Fact]
    public async Task AuthService_RefreshToken_RotatesTokensAndRevokesOldToken()
    {
        var rawOldToken = "raw_sample_refresh_token_12345";
        var oldHash = _jwtTokenGenerator.HashRefreshToken(rawOldToken);

        var user = new User { Id = 5, Email = "u5@test.com", FirstName = "User", LastName = "Five", IsActive = true };
        var storedToken = new RefreshToken
        {
            Id = 10,
            UserId = 5,
            User = user,
            TokenHash = oldHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetRefreshTokenByHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        RefreshToken? newSavedToken = null;
        userRepoMock.Setup(r => r.AddRefreshTokenAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((r, _) => newSavedToken = r)
            .Returns(Task.CompletedTask);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var result = await authService.RefreshTokenAsync(rawOldToken);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);
        Assert.NotEqual(rawOldToken, result.RefreshToken); // New token issued

        // Verify old token was revoked
        Assert.True(storedToken.IsRevoked);
        Assert.NotNull(storedToken.RevokedAt);
        Assert.NotNull(storedToken.ReplacedByTokenHash);

        // Verify new token was added
        Assert.NotNull(newSavedToken);
        Assert.Equal(5, newSavedToken.UserId);
        userRepoMock.Verify(r => r.UpdateRefreshTokenAsync(storedToken, It.IsAny<CancellationToken>()), Times.Once);
        userRepoMock.Verify(r => r.AddRefreshTokenAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthService_RefreshToken_WhenCompromisedTokenReused_RevokesAllSessions()
    {
        var rawOldToken = "compromised_token";
        var oldHash = _jwtTokenGenerator.HashRefreshToken(rawOldToken);

        var storedToken = new RefreshToken
        {
            Id = 10,
            UserId = 8,
            TokenHash = oldHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddHours(-2) // Already revoked!
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetRefreshTokenByHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.RefreshTokenAsync(rawOldToken));

        // Verifies all user sessions are revoked for security
        userRepoMock.Verify(r => r.RevokeAllUserRefreshTokensAsync(8, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthService_Logout_RevokesTargetRefreshToken()
    {
        var rawToken = "token_to_logout";
        var hash = _jwtTokenGenerator.HashRefreshToken(rawToken);

        var storedToken = new RefreshToken
        {
            Id = 15,
            UserId = 3,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetRefreshTokenByHashAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var success = await authService.LogoutAsync(rawToken);

        Assert.True(success);
        Assert.True(storedToken.IsRevoked);
        userRepoMock.Verify(r => r.UpdateRefreshTokenAsync(storedToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 2. User Profile & Preferences Tests ─────────────────────────────────

    [Fact]
    public async Task UsersController_GetProfile_ReturnsProfileAndPreferences()
    {
        var authServiceMock = new Mock<IAuthService>();
        var profileDto = new UserProfileDto(
            12,
            "user12@test.com",
            "Deniz",
            "Yilmaz",
            true,
            DateTime.UtcNow,
            DateTime.UtcNow,
            new UserPreferencesDto(true, "tr", 10, "Europe/Istanbul", null),
            true
        );

        authServiceMock.Setup(s => s.GetUserProfileAsync(12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileDto);

        var controller = new UsersController(
            new Mock<IUserTelegramService>().Object,
            authServiceMock.Object,
            new Mock<ILogger<UsersController>>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "12") }, "Test"))
            }
        };

        var result = await controller.GetProfile(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<UserProfileDto>(ok.Value);

        Assert.Equal("Deniz", returned.FirstName);
        Assert.Equal("Europe/Istanbul", returned.Preferences.Timezone);
        Assert.True(returned.HasTelegramConfigured);
    }

    [Fact]
    public async Task UsersController_UpdateProfile_UpdatesPreferencesSuccessfully()
    {
        var authServiceMock = new Mock<IAuthService>();
        var updateReq = new UpdateProfileRequest(
            FirstName: "Deniz Updated",
            LastName: "Yilmaz",
            Preferences: new UserPreferencesDto(false, "en", 15, "UTC", "push_token_abc")
        );

        var updatedDto = new UserProfileDto(
            12,
            "user12@test.com",
            "Deniz Updated",
            "Yilmaz",
            true,
            DateTime.UtcNow,
            DateTime.UtcNow,
            new UserPreferencesDto(false, "en", 15, "UTC", "push_token_abc"),
            false
        );

        authServiceMock.Setup(s => s.UpdateUserProfileAsync(12, updateReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var controller = new UsersController(
            new Mock<IUserTelegramService>().Object,
            authServiceMock.Object,
            new Mock<ILogger<UsersController>>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "12") }, "Test"))
            }
        };

        var result = await controller.UpdateProfile(updateReq, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<UserProfileDto>(ok.Value);

        Assert.Equal("Deniz Updated", returned.FirstName);
        Assert.Equal("en", returned.Preferences.NotificationLanguage);
        Assert.Equal(15, returned.Preferences.DefaultCheckIntervalMinutes);
        Assert.Equal("push_token_abc", returned.Preferences.PushNotificationToken);
    }

    // ── 3. Dashboard Summary Tests ──────────────────────────────────────────

    [Fact]
    public async Task DashboardController_GetSummary_ReturnsUserScopedMetrics()
    {
        var dashboardServiceMock = new Mock<IDashboardService>();
        var summaryDto = new DashboardSummaryDto(
            TotalMonitors: 5,
            ActiveMonitors: 4,
            PausedMonitors: 1,
            AvailableItems: 2,
            NotificationsToday: 3,
            LastNotificationAt: DateTime.UtcNow
        );

        dashboardServiceMock.Setup(s => s.GetSummaryAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryDto);

        var controller = new DashboardController(dashboardServiceMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "9") }, "Test"))
            }
        };

        var result = await controller.GetSummary(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<DashboardSummaryDto>(ok.Value);

        Assert.Equal(5, returned.TotalMonitors);
        Assert.Equal(4, returned.ActiveMonitors);
        Assert.Equal(1, returned.PausedMonitors);
        Assert.Equal(2, returned.AvailableItems);
        Assert.Equal(3, returned.NotificationsToday);
        dashboardServiceMock.Verify(s => s.GetSummaryAsync(9, It.IsAny<CancellationToken>()), Times.Once);
    }
}
