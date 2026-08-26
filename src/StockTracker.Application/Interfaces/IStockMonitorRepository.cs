using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

public interface IStockMonitorRepository
{
    Task<StockMonitor> AddAsync(StockMonitor monitor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMonitor>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMonitor>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<StockMonitor>> GetPagedByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<StockMonitor?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<StockMonitor?> GetByIdAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<StockMonitor?> GetByIdWithStatesAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMonitor>> GetDueMonitorsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockMonitor monitor, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, int userId, CancellationToken cancellationToken = default);

    Task AddNotificationHistoryAsync(StockNotificationHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockNotificationHistory>> GetNotificationHistoriesByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<StockNotificationHistory>> GetPagedNotificationHistoriesByUserIdAsync(int userId, NotificationQueryParams query, CancellationToken cancellationToken = default);
    Task<bool> HasRecentNotificationAsync(int monitorId, string variantName, TimeSpan window, CancellationToken cancellationToken = default);
}
