using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Persistence;

namespace StockTracker.Infrastructure.Services;

public class UsageLimitService : IUsageLimitService
{
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly AppDbContext _context;
    private readonly ILogger<UsageLimitService> _logger;

    public UsageLimitService(
        ISubscriptionRepository subscriptionRepo,
        AppDbContext context,
        ILogger<UsageLimitService> logger)
    {
        _subscriptionRepo = subscriptionRepo;
        _context = context;
        _logger = logger;
    }

    private static string GetTodayDateKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    public async Task<SubscriptionPlan> GetEffectivePlanForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepo.GetActiveSubscriptionByUserIdAsync(userId, cancellationToken);
        if (subscription != null && subscription.Plan != null && (!subscription.ExpiresAt.HasValue || subscription.ExpiresAt.Value > DateTime.UtcNow))
        {
            return subscription.Plan;
        }

        // Fallback to default FREE plan from DB
        var freePlan = await _subscriptionRepo.GetPlanByNameAsync("FREE", cancellationToken);
        if (freePlan != null)
        {
            return freePlan;
        }

        // Ultimate in-memory fallback defaults
        return new SubscriptionPlan
        {
            Name = "FREE",
            MaxActiveMonitors = 5,
            MaxTotalMonitors = 10,
            MinCheckIntervalMinutes = 60,
            TelegramEnabled = true,
            MaxNotificationsPerDay = 20,
            MaxInspectRequestsPerDay = 20
        };
    }

    public async Task<(bool Allowed, string? ErrorCode, string? Message)> CanCreateMonitorAsync(int userId, int requestedIntervalMinutes, CancellationToken cancellationToken = default)
    {
        var plan = await GetEffectivePlanForUserAsync(userId, cancellationToken);

        var monitors = await _context.StockMonitors
            .Where(m => m.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalCount = monitors.Count;
        var activeCount = monitors.Count(m => m.IsActive);

        if (totalCount >= plan.MaxTotalMonitors)
        {
            return (false, "PLAN_LIMIT_REACHED", $"{plan.Name} planınızdaki maksimum toplam takip limitine ({plan.MaxTotalMonitors}) ulaştınız.");
        }

        if (activeCount >= plan.MaxActiveMonitors)
        {
            return (false, "PLAN_LIMIT_REACHED", $"{plan.Name} planınızdaki aktif takip limitine ({plan.MaxActiveMonitors}) ulaştınız. Mevcut bir takibi durdurabilir veya planınızı yükseltebilirsiniz.");
        }

        if (requestedIntervalMinutes < plan.MinCheckIntervalMinutes)
        {
            return (false, "CHECK_INTERVAL_NOT_ALLOWED", $"Seçilen kontrol sıklığı ({requestedIntervalMinutes} dk) {plan.Name} planınızın minimum sınırından ({plan.MinCheckIntervalMinutes} dk) küçük olamaz.");
        }

        return (true, null, null);
    }

    public async Task<(bool Allowed, string? ErrorCode, string? Message)> CanActivateMonitorAsync(int userId, int monitorId, CancellationToken cancellationToken = default)
    {
        var plan = await GetEffectivePlanForUserAsync(userId, cancellationToken);

        var activeCount = await _context.StockMonitors
            .CountAsync(m => m.UserId == userId && m.IsActive && m.Id != monitorId, cancellationToken);

        if (activeCount >= plan.MaxActiveMonitors)
        {
            return (false, "PLAN_LIMIT_REACHED", $"{plan.Name} planınızdaki aktif takip limitine ({plan.MaxActiveMonitors}) ulaştınız.");
        }

        return (true, null, null);
    }

    public async Task<(bool Allowed, string? ErrorCode, string? Message)> CanUpdateMonitorIntervalAsync(int userId, int requestedIntervalMinutes, CancellationToken cancellationToken = default)
    {
        var plan = await GetEffectivePlanForUserAsync(userId, cancellationToken);
        if (requestedIntervalMinutes < plan.MinCheckIntervalMinutes)
        {
            return (false, "CHECK_INTERVAL_NOT_ALLOWED", $"Seçilen kontrol sıklığı ({requestedIntervalMinutes} dk) {plan.Name} planınızın minimum sınırından ({plan.MinCheckIntervalMinutes} dk) küçük olamaz.");
        }

        return (true, null, null);
    }

    public async Task<(bool Allowed, string? ErrorCode, string? Message)> CanInspectProductAsync(int? userId, CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue || userId.Value <= 0)
        {
            return (true, null, null); // Handled by global IP rate limiter
        }

        var plan = await GetEffectivePlanForUserAsync(userId.Value, cancellationToken);
        var usage = await _subscriptionRepo.GetOrCreateDailyUsageAsync(userId.Value, GetTodayDateKey(), cancellationToken);

        if (usage.InspectRequestsCount >= plan.MaxInspectRequestsPerDay)
        {
            return (false, "DAILY_INSPECT_LIMIT_REACHED", $"{plan.Name} planınızdaki günlük ürün inceleme limitine ({plan.MaxInspectRequestsPerDay}) ulaştınız.");
        }

        return (true, null, null);
    }

    public async Task<bool> CanSendNotificationAsync(int userId, CancellationToken cancellationToken = default)
    {
        var plan = await GetEffectivePlanForUserAsync(userId, cancellationToken);
        if (!plan.TelegramEnabled) return false;

        var usage = await _subscriptionRepo.GetOrCreateDailyUsageAsync(userId, GetTodayDateKey(), cancellationToken);
        return usage.NotificationsCount < plan.MaxNotificationsPerDay;
    }

    public async Task RecordInspectUsageAsync(int? userId, CancellationToken cancellationToken = default)
    {
        if (userId.HasValue && userId.Value > 0)
        {
            await _subscriptionRepo.IncrementInspectUsageAsync(userId.Value, GetTodayDateKey(), cancellationToken);
        }
    }

    public async Task RecordNotificationUsageAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _subscriptionRepo.IncrementNotificationUsageAsync(userId, GetTodayDateKey(), cancellationToken);
    }

    public async Task<UsageSummaryDto> GetUsageSummaryAsync(int userId, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepo.GetActiveSubscriptionByUserIdAsync(userId, cancellationToken);
        var plan = await GetEffectivePlanForUserAsync(userId, cancellationToken);
        var status = subscription?.Status.ToString() ?? "Active";

        var monitors = await _context.StockMonitors
            .Where(m => m.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var totalMonitors = monitors.Count;
        var activeMonitors = monitors.Count(m => m.IsActive);

        var usageRecord = await _subscriptionRepo.GetOrCreateDailyUsageAsync(userId, GetTodayDateKey(), cancellationToken);

        var limitsDto = new PlanLimitsDto(
            plan.MaxActiveMonitors,
            plan.MaxTotalMonitors,
            plan.MinCheckIntervalMinutes,
            plan.TelegramEnabled,
            plan.MaxNotificationsPerDay,
            plan.MaxInspectRequestsPerDay
        );

        var usageMetrics = new UsageMetricsDto(
            activeMonitors,
            totalMonitors,
            usageRecord.NotificationsCount,
            usageRecord.InspectRequestsCount
        );

        return new UsageSummaryDto(
            plan.Name,
            status,
            limitsDto,
            usageMetrics
        );
    }
}
