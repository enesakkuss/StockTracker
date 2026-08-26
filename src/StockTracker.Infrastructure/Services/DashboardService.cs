using Microsoft.EntityFrameworkCore;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Infrastructure.Persistence;

namespace StockTracker.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(int userId, CancellationToken cancellationToken = default)
    {
        var monitors = await _context.StockMonitors
            .Where(m => m.UserId == userId)
            .Include(m => m.VariantStates)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalMonitors = monitors.Count;
        var activeMonitors = monitors.Count(m => m.IsActive);
        var pausedMonitors = totalMonitors - activeMonitors;

        var availableItems = monitors
            .SelectMany(m => m.VariantStates)
            .Count(v => v.IsAvailable);

        var todayUtc = DateTime.UtcNow.Date;

        var notifications = await _context.StockNotificationHistories
            .Where(h => h.UserId == userId || (h.StockMonitor != null && h.StockMonitor.UserId == userId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var notificationsToday = notifications.Count(n => n.NotificationSentAt >= todayUtc);
        var lastNotificationAt = notifications.MaxBy(n => n.NotificationSentAt)?.NotificationSentAt;

        return new DashboardSummaryDto(
            totalMonitors,
            activeMonitors,
            pausedMonitors,
            availableItems,
            notificationsToday,
            lastNotificationAt
        );
    }
}
