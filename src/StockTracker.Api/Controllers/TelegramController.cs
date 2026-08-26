using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramService _telegramService;
    private readonly IUserRepository? _userRepository;
    private readonly ISecretProtector? _secretProtector;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        ITelegramService telegramService,
        ILogger<TelegramController> logger,
        IUserRepository? userRepository = null,
        ISecretProtector? secretProtector = null)
    {
        _telegramService = telegramService;
        _logger = logger;
        _userRepository = userRepository;
        _secretProtector = secretProtector;
    }

    /// <summary>
    /// Tests user-provided Telegram bot credentials by verifying the token and sending a test message.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(TelegramTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TelegramTestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestConnection(
        [FromBody] TelegramTestRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new TelegramTestResponse(false, "Geçersiz istek."));
        }

        string botToken = request.BotToken?.Trim() ?? string.Empty;
        string chatId = request.ChatId?.Trim() ?? string.Empty;

        // If botToken is empty and user is authenticated, fall back to user's saved protected token
        if (string.IsNullOrWhiteSpace(botToken) && HttpContext != null && User?.Identity?.IsAuthenticated == true && _userRepository != null && _secretProtector != null)
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idStr, out var userId) && userId > 0)
            {
                var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
                if (user != null && !string.IsNullOrWhiteSpace(user.ProtectedTelegramBotToken))
                {
                    try
                    {
                        botToken = _secretProtector.Unprotect(user.ProtectedTelegramBotToken);
                        if (string.IsNullOrWhiteSpace(chatId))
                        {
                            chatId = user.TelegramChatId ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to unprotect saved token for test connection");
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(botToken))
        {
            return BadRequest(new TelegramTestResponse(false, "Bot Token boş olamaz."));
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            return BadRequest(new TelegramTestResponse(false, "Chat ID boş olamaz."));
        }

        var response = await _telegramService.TestConnectionAsync(
            botToken,
            chatId,
            cancellationToken);

        return Ok(response);
    }
}
