using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
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
    /// Giriş yapmış kullanıcının sayfalanmış ödeme geçmişini döndürür.
    /// </summary>
    [Authorize]
    [HttpGet("history")]
    [ProducesResponseType(typeof(PagedResponse<PaymentTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaymentHistory([FromQuery] PaymentHistoryQueryParams @params, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var paged = await _paymentService.GetUserPaymentHistoryAsync(userId, @params.Page, @params.PageSize, cancellationToken);
        return Ok(paged);
    }

    /// <summary>
    /// Belirtilen ödeme işleminin detaylarını döndürür (IDOR korumalı).
    /// </summary>
    [Authorize]
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PaymentTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentById(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var tx = await _paymentService.GetPaymentTransactionByIdAsync(id, userId, cancellationToken);
        if (tx is null)
        {
            return NotFound(new { error = $"ID {id} olan ödeme işlemi bulunamadı." });
        }

        return Ok(tx);
    }

    /// <summary>
    /// Ödeme sağlayıcılarından gelen webhook bildirimlerini işler (JWT gerektirmez, imza kontrolü yapılır).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("webhook/{provider}")]
    [ProducesResponseType(typeof(WebhookResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleWebhook(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        // Extract signature header
        string? signature = null;
        if (Request.Headers.TryGetValue("X-Signature", out var sigVal))
            signature = sigVal.FirstOrDefault();
        else if (Request.Headers.TryGetValue("X-Iyzico-Signature", out var iyziSig))
            signature = iyziSig.FirstOrDefault();
        else if (Request.Headers.TryGetValue("Authorization", out var authVal))
            signature = authVal.FirstOrDefault();

        try
        {
            var result = await _paymentService.ProcessWebhookAsync(provider, payload, signature, cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Webhook unauthorized: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook for provider {Provider}", provider);
            return BadRequest(new { error = "Webhook işlenemedi." });
        }
    }

    /// <summary>
    /// Belirtilen ödeme işlemini iade eder (Refund).
    /// </summary>
    [Authorize]
    [HttpPost("{id:int}/refund")]
    [ProducesResponseType(typeof(PaymentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Refund(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _paymentService.RefundTransactionAsync(id, userId, cancellationToken: cancellationToken);
        if (!result.Success && result.ErrorCode == "NOT_FOUND")
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(result);
    }
}
