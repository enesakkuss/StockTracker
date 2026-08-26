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
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        ITelegramService telegramService,
        ILogger<TelegramController> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    /// <summary>
    /// Tests user-provided Telegram bot credentials by verifying the token and sending a test message.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/telegram/test
    ///     {
    ///        "botToken": "123456789:AA...",
    ///        "chatId": "123456789"
    ///     }
    ///
    /// </remarks>
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

        if (string.IsNullOrWhiteSpace(request.BotToken))
        {
            return BadRequest(new TelegramTestResponse(false, "Bot Token boş olamaz."));
        }

        if (string.IsNullOrWhiteSpace(request.ChatId))
        {
            return BadRequest(new TelegramTestResponse(false, "Chat ID boş olamaz."));
        }

        var response = await _telegramService.TestConnectionAsync(
            request.BotToken,
            request.ChatId,
            cancellationToken);

        return Ok(response);
    }
}
