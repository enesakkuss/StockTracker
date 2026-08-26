using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Controllers;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Services;

namespace StockTracker.Tests;

public class UserAuthenticationAndMultiUserTests
{
    private readonly IConfiguration _config;
    private readonly PasswordHasher _passwordHasher = new();
    private readonly JwtTokenGenerator _jwtTokenGenerator;

    public UserAuthenticationAndMultiUserTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:SecretKey", "Test_Super_Secret_Key_For_Unit_Testing_32_Bytes_Long_2026!" },
                { "Jwt:Issuer", "StockTrackerTest" },
                { "Jwt:Audience", "StockTrackerTestAudience" },
                { "Jwt:ExpirationMinutes", "60" }
            })
            .Build();

        _jwtTokenGenerator = new JwtTokenGenerator(_config);
    }

    // ── 1. Password Hasher Tests ────────────────────────────────────────────

    [Fact]
    public void PasswordHasher_HashesAndVerifiesPasswordCorrectly()
    {
        var password = "SecurePassword123!";
        var hash = _passwordHasher.HashPassword(password);

        Assert.NotNull(hash);
        Assert.NotEqual(password, hash);
        Assert.True(_passwordHasher.VerifyPassword(password, hash));
        Assert.False(_passwordHasher.VerifyPassword("WrongPassword123!", hash));
    }

    // ── 2. JWT Generator Tests ──────────────────────────────────────────────

    [Fact]
    public void JwtTokenGenerator_GeneratesValidTokenWithExpectedClaims()
    {
        var user = new User
        {
            Id = 42,
            Email = "tester@example.com",
            FirstName = "John",
            LastName = "Doe",
            IsActive = true
        };

        var (token, expiration) = _jwtTokenGenerator.GenerateToken(user);

        Assert.NotNull(token);
        Assert.True(expiration > DateTime.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("StockTrackerTest", jwt.Issuer);
        Assert.Contains("StockTrackerTestAudience", jwt.Audiences);
        
        var idClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid" || c.Type == "sub");
        Assert.NotNull(idClaim);
        Assert.Equal("42", idClaim.Value);

        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email");
        Assert.NotNull(emailClaim);
        Assert.Equal("tester@example.com", emailClaim.Value);
    }

    // ── 3. Register Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task AuthService_RegisterAsync_WhenValid_ReturnsTokenAndUser()
    {
        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.EmailExistsAsync("newuser@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        User? capturedUser = null;
        userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => { u.Id = 10; capturedUser = u; })
            .ReturnsAsync((User u, CancellationToken _) => u);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var req = new RegisterRequest("newuser@example.com", "Password123!", "Alice", "Smith");
        var response = await authService.RegisterAsync(req);

        Assert.NotNull(response.Token);
        Assert.Equal(10, response.User.Id);
        Assert.Equal("newuser@example.com", response.User.Email);
        Assert.Equal("Alice", response.User.FirstName);
        Assert.NotNull(capturedUser);
        Assert.True(_passwordHasher.VerifyPassword("Password123!", capturedUser.PasswordHash));
    }

    [Fact]
    public async Task AuthService_RegisterAsync_WhenDuplicateEmail_ThrowsInvalidOperationException()
    {
        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.EmailExistsAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);
        var req = new RegisterRequest("existing@example.com", "Password123!", "Alice", "Smith");

        await Assert.ThrowsAsync<InvalidOperationException>(() => authService.RegisterAsync(req));
    }

    [Theory]
    [InlineData("invalid-email", "Password123!", "Alice", "Smith")]
    [InlineData("", "Password123!", "Alice", "Smith")]
    [InlineData("test@example.com", "123", "Alice", "Smith")] // Weak password (< 6 chars)
    [InlineData("test@example.com", "Password123!", "", "Smith")] // Missing FirstName
    public async Task AuthService_RegisterAsync_WhenInvalidInputs_ThrowsArgumentException(string email, string password, string first, string last)
    {
        var userRepoMock = new Mock<IUserRepository>();
        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var req = new RegisterRequest(email, password, first, last);
        await Assert.ThrowsAsync<ArgumentException>(() => authService.RegisterAsync(req));
    }

    // ── 4. Login Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task AuthService_LoginAsync_WhenValidCredentials_ReturnsToken()
    {
        var passwordHash = _passwordHasher.HashPassword("CorrectPassword123!");
        var user = new User
        {
            Id = 5,
            Email = "login@example.com",
            PasswordHash = passwordHash,
            FirstName = "Bob",
            LastName = "Taylor",
            IsActive = true
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByEmailAsync("login@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        var response = await authService.LoginAsync(new LoginRequest("login@example.com", "CorrectPassword123!"));

        Assert.NotNull(response.Token);
        Assert.Equal(5, response.User.Id);
        userRepoMock.Verify(r => r.UpdateAsync(It.Is<User>(u => u.LastLoginAt != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthService_LoginAsync_WhenWrongPassword_ThrowsUnauthorizedAccessException()
    {
        var passwordHash = _passwordHasher.HashPassword("CorrectPassword123!");
        var user = new User
        {
            Id = 5,
            Email = "login@example.com",
            PasswordHash = passwordHash,
            IsActive = true
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByEmailAsync("login@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("login@example.com", "WrongPassword!")));
    }

    [Fact]
    public async Task AuthService_LoginAsync_WhenUnknownUser_ThrowsUnauthorizedAccessException()
    {
        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByEmailAsync("unknown@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("unknown@example.com", "Password123!")));
    }

    [Fact]
    public async Task AuthService_LoginAsync_WhenInactiveUser_ThrowsUnauthorizedAccessException()
    {
        var passwordHash = _passwordHasher.HashPassword("CorrectPassword123!");
        var user = new User
        {
            Id = 5,
            Email = "inactive@example.com",
            PasswordHash = passwordHash,
            IsActive = false // Deactivated
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByEmailAsync("inactive@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var authService = new AuthService(userRepoMock.Object, _passwordHasher, _jwtTokenGenerator, new Mock<ILogger<AuthService>>().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("inactive@example.com", "CorrectPassword123!")));
    }

    // ── 5. User Telegram Settings & Masking Tests ───────────────────────────

    [Fact]
    public async Task UserTelegramService_GetTelegramSettingsAsync_MasksSecretToken()
    {
        var secretProtectorMock = new Mock<ISecretProtector>();
        secretProtectorMock.Setup(s => s.Unprotect("enc_token_123"))
            .Returns("123456789:ABCDEFGH12345678");

        var user = new User
        {
            Id = 1,
            Email = "u@example.com",
            ProtectedTelegramBotToken = "enc_token_123",
            TelegramChatId = "987654321"
        };

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = new UserTelegramService(userRepoMock.Object, secretProtectorMock.Object, new Mock<ILogger<UserTelegramService>>().Object);

        var settings = await service.GetTelegramSettingsAsync(1);

        Assert.True(settings.IsConfigured);
        Assert.Equal("987654321", settings.ChatId);
        Assert.NotNull(settings.MaskedBotToken);
        Assert.DoesNotContain("ABCDEFGH", settings.MaskedBotToken); // Secret token is masked
        Assert.StartsWith("1234", settings.MaskedBotToken);
        Assert.EndsWith("5678", settings.MaskedBotToken);
    }

    // ── 6. Multi-User Monitor Isolation Tests ───────────────────────────────

    [Fact]
    public async Task StockMonitorService_UserIsolation_ReturnsOnlyUserOwnedMonitors()
    {
        var repoMock = new Mock<IStockMonitorRepository>();
        var user1Monitors = new List<StockMonitor>
        {
            new() { Id = 1, UserId = 10, ProductName = "User 10 Item", ProductUrl = "https://www.zara.com/p1", Store = "Zara" }
        };

        repoMock.Setup(r => r.GetAllByUserIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user1Monitors);

        var service = new StockMonitorService(
            repoMock.Object,
            new Mock<ISecretProtector>().Object,
            new Mock<IStoreAdapterResolver>().Object,
            _config,
            new Mock<ILogger<StockMonitorService>>().Object);

        var result = await service.GetMonitorsAsync(10);

        Assert.Single(result);
        Assert.Equal("User 10 Item", result[0].ProductName);
    }

    [Fact]
    public async Task MonitorsController_WhenUserAttemptsToAccessOtherUserMonitor_Returns404()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        // User 2 tries to access monitor 99 belonging to User 1 -> returns null
        monitorServiceMock.Setup(s => s.GetMonitorByIdAsync(99, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMonitorDto?)null);

        var controller = new MonitorsController(
            monitorServiceMock.Object,
            new Mock<IStockCheckerService>().Object,
            new Mock<ILogger<MonitorsController>>().Object);

        // Set HttpContext Claims for User ID = 2
        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "2"),
            new Claim(ClaimTypes.Email, "user2@example.com")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = userClaims }
        };

        var actionResult = await controller.GetById(99, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(actionResult);
    }

    [Fact]
    public async Task MonitorsController_WhenUserCreatesMonitor_AssociatesWithAuthenticatedUserId()
    {
        var monitorServiceMock = new Mock<IStockMonitorService>();
        var req = new CreateMonitorRequest(
            ProductUrl: "https://www.zara.com/tr/item-p1",
            Store: "Zara",
            ProductName: "Keten Ceket",
            ImageUrl: null,
            SelectedVariants: new List<string> { "M" },
            TelegramBotToken: "123:ABC",
            TelegramChatId: "999",
            CheckIntervalMinutes: 10
        );

        monitorServiceMock.Setup(s => s.CreateMonitorAsync(7, req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockMonitorDto(
                Id: 50,
                ProductUrl: req.ProductUrl,
                Store: "Zara",
                ProductName: "Keten Ceket",
                ImageUrl: null,
                SelectedVariants: new List<string> { "M" },
                CheckIntervalMinutes: 10,
                IsActive: true,
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: null,
                LastCheckedAt: null,
                NextCheckAt: DateTime.UtcNow,
                LastCheckStatus: null,
                LastCheckError: null,
                LastNotifiedAt: null,
                LastNotifiedVariant: null
            ));

        var controller = new MonitorsController(
            monitorServiceMock.Object,
            new Mock<IStockCheckerService>().Object,
            new Mock<ILogger<MonitorsController>>().Object);

        var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Email, "user7@example.com")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = userClaims }
        };

        var actionResult = await controller.Create(req, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult);
        var createdDto = Assert.IsType<StockMonitorDto>(createdResult.Value);

        Assert.Equal(50, createdDto.Id);
        monitorServiceMock.Verify(s => s.CreateMonitorAsync(7, req, It.IsAny<CancellationToken>()), Times.Once);
    }
}
