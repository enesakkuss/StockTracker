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
public class MonitorsController : ControllerBase
{
    private readonly IStockMonitorService _monitorService;
    private readonly IStockCheckerService _checkerService;
    private readonly ILogger<MonitorsController> _logger;
    private readonly IUsageLimitService? _usageLimitService;

    public MonitorsController(
        IStockMonitorService monitorService,
        IStockCheckerService checkerService,
        ILogger<MonitorsController> logger,
        IUsageLimitService? usageLimitService = null)
    {
        _monitorService = monitorService;
        _checkerService = checkerService;
        _logger = logger;
        _usageLimitService = usageLimitService;
    }

    private int GetCurrentUserId()
    {
        if (HttpContext == null || User == null) return 1;
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idStr, out var id) && id > 0) return id;
        return 1;
    }

    /// <summary>
    /// Gets paginated stock monitors for the authenticated user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<StockMonitorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll([FromQuery] PaginationParams @params, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var paged = await _monitorService.GetPagedMonitorsAsync(userId, @params.Page, @params.PageSize, cancellationToken);
        return Ok(paged);
    }

    /// <summary>
    /// Gets a single stock monitor by ID for the authenticated user.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StockMonitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var monitor = await _monitorService.GetMonitorByIdAsync(id, userId, cancellationToken);
        if (monitor is null)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        return Ok(monitor);
    }

    /// <summary>
    /// Creates a new stock monitor for a product and selected size variants for the authenticated user.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockMonitorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMonitorRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // 1. Check plan usage limits before creating monitor
        if (_usageLimitService != null)
        {
            var (allowed, errorCode, message) = await _usageLimitService.CanCreateMonitorAsync(userId, request.CheckIntervalMinutes, cancellationToken);
            if (!allowed)
            {
                return UnprocessableEntity(new { error = message, code = errorCode });
            }
        }

        try
        {
            var created = await _monitorService.CreateMonitorAsync(userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Monitor creation validation failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return UnprocessableEntity(new { error = ex.Message, code = "UNSUPPORTED_STORE" });
        }
    }

    /// <summary>
    /// Updates selected variants, interval, or telegram settings for the specified monitor.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(StockMonitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMonitorRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (request.CheckIntervalMinutes.HasValue && _usageLimitService != null)
        {
            var (allowed, errorCode, message) = await _usageLimitService.CanUpdateMonitorIntervalAsync(userId, request.CheckIntervalMinutes.Value, cancellationToken);
            if (!allowed)
            {
                return UnprocessableEntity(new { error = message, code = errorCode });
            }
        }

        var updated = await _monitorService.UpdateMonitorAsync(id, userId, request, cancellationToken);
        if (updated is null)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        return Ok(updated);
    }

    /// <summary>
    /// Starts/resumes monitoring for the specified monitor.
    /// </summary>
    [HttpPost("{id:int}/resume")]
    [HttpPost("{id:int}/start")]
    [ProducesResponseType(typeof(StockMonitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Resume(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (_usageLimitService != null)
        {
            var (allowed, errorCode, message) = await _usageLimitService.CanActivateMonitorAsync(userId, id, cancellationToken);
            if (!allowed)
            {
                return UnprocessableEntity(new { error = message, code = errorCode });
            }
        }

        var updated = await _monitorService.StartMonitorAsync(id, userId, cancellationToken);
        if (updated is null)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        return Ok(updated);
    }

    /// <summary>
    /// Pauses/stops monitoring for the specified monitor.
    /// </summary>
    [HttpPost("{id:int}/pause")]
    [HttpPost("{id:int}/stop")]
    [ProducesResponseType(typeof(StockMonitorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var updated = await _monitorService.StopMonitorAsync(id, userId, cancellationToken);
        if (updated is null)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        return Ok(updated);
    }

    /// <summary>
    /// Triggers an immediate manual stock check for the specified monitor.
    /// </summary>
    [HttpPost("{id:int}/check")]
    [ProducesResponseType(typeof(ManualCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Check(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var monitor = await _monitorService.GetMonitorByIdAsync(id, userId, cancellationToken);
        if (monitor is null)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        var result = await _checkerService.CheckMonitorByIdAsync(id, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deletes the specified stock monitor belonging to the authenticated user.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var deleted = await _monitorService.DeleteMonitorAsync(id, userId, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { error = $"ID {id} olan stok takibi bulunamadı." });
        }

        return NoContent();
    }
}
