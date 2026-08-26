using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserTelegramService _telegramService;
    private readonly IAuthService _authService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserTelegramService telegramService,
        IAuthService authService,
        ILogger<UsersController> logger)
    {
        _telegramService = telegramService;
        _authService = authService;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        if (HttpContext == null || User == null) return 1;
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var id) && id > 0) return id;
        return 1;
    }

    /// <summary>
    /// Giriş yapmış kullanıcının profil ve tercih bilgilerini döndürür.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var profile = await _authService.GetUserProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return NotFound(new { error = "Kullanıcı profili bulunamadı." });
        }

        return Ok(profile);
    }

    /// <summary>
    /// Giriş yapmış kullanıcının ad, soyad ve tercihlerini günceller.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        try
        {
            var updated = await _authService.UpdateUserProfileAsync(userId, request, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Giriş yapmış kullanıcının Telegram yapılandırmasını (maskelenmiş token ile) döndürür.
    /// </summary>
    [HttpGet("me/telegram")]
    [ProducesResponseType(typeof(UserTelegramSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTelegramSettings(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var settings = await _telegramService.GetTelegramSettingsAsync(userId, cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Giriş yapmış kullanıcının Telegram Bot Token ve Chat ID bilgilerini günceller.
    /// </summary>
    [HttpPut("me/telegram")]
    [ProducesResponseType(typeof(UserTelegramSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateTelegramSettings([FromBody] UpdateTelegramSettingsRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        try
        {
            var settings = await _telegramService.UpdateTelegramSettingsAsync(userId, request, cancellationToken);
            return Ok(settings);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Giriş yapmış kullanıcının kayıtlı Telegram bilgilerini siler.
    /// </summary>
    [HttpDelete("me/telegram")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteTelegramSettings(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _telegramService.DeleteTelegramSettingsAsync(userId, cancellationToken);
        return NoContent();
    }
}
