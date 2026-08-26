using StockTracker.Application.Common;
using StockTracker.Application.DTOs;

namespace StockTracker.Application.Interfaces;

public interface IStockMonitorService
{
    Task<StockMonitorDto> CreateMonitorAsync(int userId, CreateMonitorRequest request, CancellationToken cancellationToken = default);
    Task<StockMonitorDto> CreateMonitorAsync(CreateMonitorRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMonitorDto>> GetMonitorsAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMonitorDto>> GetMonitorsAsync(CancellationToken cancellationToken = default);
    Task<PagedResponse<StockMonitorDto>> GetPagedMonitorsAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<StockMonitorDto?> GetMonitorByIdAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<StockMonitorDto?> GetMonitorByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<StockMonitorDto?> UpdateMonitorAsync(int id, int userId, UpdateMonitorRequest request, CancellationToken cancellationToken = default);

    Task<StockMonitorDto?> StartMonitorAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<StockMonitorDto?> StartMonitorAsync(int id, CancellationToken cancellationToken = default);

    Task<StockMonitorDto?> StopMonitorAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<StockMonitorDto?> StopMonitorAsync(int id, CancellationToken cancellationToken = default);

    Task<StockMonitorDto?> PauseMonitorAsync(int id, int userId, CancellationToken cancellationToken = default) => StopMonitorAsync(id, userId, cancellationToken);
    Task<StockMonitorDto?> ResumeMonitorAsync(int id, int userId, CancellationToken cancellationToken = default) => StartMonitorAsync(id, userId, cancellationToken);

    Task<bool> DeleteMonitorAsync(int id, int userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteMonitorAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResponse<NotificationHistoryDto>> GetNotificationHistoriesAsync(int userId, NotificationQueryParams query, CancellationToken cancellationToken = default);
}
