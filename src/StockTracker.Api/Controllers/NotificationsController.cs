using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;

namespace StockTracker.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly IStockMonitorService _monitorService;

    public NotificationsController(IStockMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    private int GetCurrentUserId()
    {
        if (HttpContext == null || User == null) return 1;
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var id) && id > 0) return id;
        return 1;
    }

    /// <summary>
    /// Giriş yapmış kullanıcının bildirim geçmişini sayfalanmış ve filtrelenebilir olarak döndürür.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<NotificationHistoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetNotifications([FromQuery] NotificationQueryParams query, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var correlationId = HttpContext?.Items["CorrelationId"]?.ToString();

        var paged = await _monitorService.GetNotificationHistoriesAsync(userId, query, cancellationToken);
        return Ok(ApiResponse<PagedResponse<NotificationHistoryDto>>.Ok(paged, correlationId));
    }
}
