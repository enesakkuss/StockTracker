using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(
        ISubscriptionService subscriptionService,
        IPaymentService paymentService,
        ILogger<SubscriptionsController> logger)
    {
        _subscriptionService = subscriptionService;
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
    /// Giriş yapmış kullanıcının geçerli abonelik durumunu ve limitlerini döndürür.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySubscription(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var subscription = await _subscriptionService.GetUserSubscriptionAsync(userId, cancellationToken);
        return Ok(subscription);
    }

    /// <summary>
    /// Sistemde mevcut aktif abonelik planlarını ve limitlerini listeler.
    /// </summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var plans = await _subscriptionService.GetActivePlansAsync(cancellationToken);
        return Ok(plans);
    }

    /// <summary>
    /// Giriş yapmış kullanıcının anlık kullanım ve kalan limit istatistiklerini döndürür.
    /// </summary>
    [Authorize]
    [HttpGet("usage")]
    [ProducesResponseType(typeof(UsageSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUsage(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var usage = await _subscriptionService.GetUsageAsync(userId, cancellationToken);
        return Ok(usage);
    }

    /// <summary>
    /// Plan yükseltme / abonelik başlatma için güvenli ödeme oturumu oluşturur (Idempotent).
    /// </summary>
    [Authorize]
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(PaymentCheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // Extract idempotency key from header if not in body
        string? idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && Request.Headers.TryGetValue("X-Idempotency-Key", out var headerKey))
        {
            idempotencyKey = headerKey.FirstOrDefault();
        }

        var normalizedReq = request with { IdempotencyKey = idempotencyKey };

        try
        {
            var checkout = await _paymentService.CreateCheckoutAsync(userId, normalizedReq, cancellationToken);
            return Ok(checkout);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Kullanıcının aktif aboneliğini iptal eder (FREE plana düşürür).
    /// </summary>
    [Authorize]
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelSubscription(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await _subscriptionService.AssignPlanAsync(userId, "FREE", cancellationToken);
        return NoContent();
    }
}
