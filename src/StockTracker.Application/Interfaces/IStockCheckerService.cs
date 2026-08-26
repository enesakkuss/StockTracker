using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

public interface IStockCheckerService
{
    /// <summary>
    /// Checks stock status for a given monitor, updates its variant states, and detects changes.
    /// Handles timeouts, store resolution, error recording and schedule updating safely.
    /// </summary>
    Task<IReadOnlyList<StockChange>> CheckMonitorAsync(StockMonitor monitor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks stock status for a monitor by ID (used by manual check endpoint).
    /// </summary>
    Task<ManualCheckResponse> CheckMonitorByIdAsync(int monitorId, CancellationToken cancellationToken = default);
}
