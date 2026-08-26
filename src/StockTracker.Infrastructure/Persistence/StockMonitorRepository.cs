using Microsoft.EntityFrameworkCore;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Persistence;

public class StockMonitorRepository : IStockMonitorRepository
{
    private readonly AppDbContext _context;

    public StockMonitorRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<StockMonitor> AddAsync(StockMonitor monitor, CancellationToken cancellationToken = default)
    {
        _context.StockMonitors.Add(monitor);
        await _context.SaveChangesAsync(cancellationToken);
        return monitor;
    }

    public async Task<IReadOnlyList<StockMonitor>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .Include(m => m.VariantStates)
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockMonitor>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .Include(m => m.VariantStates)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<StockMonitor>> GetPagedByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

        var query = _context.StockMonitors
            .Include(m => m.VariantStates)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedResponse<StockMonitor>(items, totalCount, page, pageSize);
    }

    public async Task<StockMonitor?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<StockMonitor?> GetByIdAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, cancellationToken);
    }

    public async Task<StockMonitor?> GetByIdWithStatesAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .Include(m => m.VariantStates)
            .Include(m => m.NotificationHistories)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StockMonitor>> GetDueMonitorsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return await _context.StockMonitors
            .Include(m => m.VariantStates)
            .Where(m => m.IsActive && m.NextCheckAt <= utcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(StockMonitor monitor, CancellationToken cancellationToken = default)
    {
        _context.StockMonitors.Update(monitor);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var monitor = await _context.StockMonitors
            .Include(m => m.VariantStates)
            .Include(m => m.NotificationHistories)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (monitor is null) return false;

        _context.StockMonitors.Remove(monitor);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var monitor = await _context.StockMonitors
            .Include(m => m.VariantStates)
            .Include(m => m.NotificationHistories)
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, cancellationToken);

        if (monitor is null) return false;

        _context.StockMonitors.Remove(monitor);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddNotificationHistoryAsync(StockNotificationHistory history, CancellationToken cancellationToken = default)
    {
        _context.StockNotificationHistories.Add(history);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockNotificationHistory>> GetNotificationHistoriesByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.StockNotificationHistories
            .Include(h => h.StockMonitor)
            .Where(h => h.UserId == userId || (h.StockMonitor != null && h.StockMonitor.UserId == userId))
            .OrderByDescending(h => h.NotificationSentAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<StockNotificationHistory>> GetPagedNotificationHistoriesByUserIdAsync(int userId, NotificationQueryParams query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

        var dbQuery = _context.StockNotificationHistories
            .Include(h => h.StockMonitor)
            .Where(h => h.UserId == userId || (h.StockMonitor != null && h.StockMonitor.UserId == userId));

        if (query.MonitorId.HasValue && query.MonitorId.Value > 0)
        {
            dbQuery = dbQuery.Where(h => h.StockMonitorId == query.MonitorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Store))
        {
            var storeTrim = query.Store.Trim().ToLower();
            dbQuery = dbQuery.Where(h => h.StockMonitor != null && h.StockMonitor.Store.ToLower() == storeTrim);
        }

        if (query.DateFrom.HasValue)
        {
            dbQuery = dbQuery.Where(h => h.NotificationSentAt >= query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            dbQuery = dbQuery.Where(h => h.NotificationSentAt <= query.DateTo.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderByDescending(h => h.NotificationSentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedResponse<StockNotificationHistory>(items, totalCount, page, pageSize);
    }

    public async Task<bool> HasRecentNotificationAsync(int monitorId, string variantName, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.Subtract(window);
        return await _context.StockNotificationHistories
            .AnyAsync(h => h.StockMonitorId == monitorId
                        && h.VariantName == variantName
                        && h.Success
                        && h.NotificationSentAt >= threshold, cancellationToken);
    }
}
