using StockTracker.Application.DTOs;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanByIdAsync(int planId, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanByNameAsync(string planName, CancellationToken cancellationToken = default);

    Task<Subscription?> GetActiveSubscriptionByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<Subscription> AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default);
    Task UpdateSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default);

    Task<DailyUsageRecord> GetOrCreateDailyUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default);
    Task IncrementInspectUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default);
    Task IncrementNotificationUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default);
}

public interface IUsageLimitService
{
    Task<(bool Allowed, string? ErrorCode, string? Message)> CanCreateMonitorAsync(int userId, int requestedIntervalMinutes, CancellationToken cancellationToken = default);
    Task<(bool Allowed, string? ErrorCode, string? Message)> CanActivateMonitorAsync(int userId, int monitorId, CancellationToken cancellationToken = default);
    Task<(bool Allowed, string? ErrorCode, string? Message)> CanUpdateMonitorIntervalAsync(int userId, int requestedIntervalMinutes, CancellationToken cancellationToken = default);
    Task<(bool Allowed, string? ErrorCode, string? Message)> CanInspectProductAsync(int? userId, CancellationToken cancellationToken = default);
    Task<bool> CanSendNotificationAsync(int userId, CancellationToken cancellationToken = default);

    Task RecordInspectUsageAsync(int? userId, CancellationToken cancellationToken = default);
    Task RecordNotificationUsageAsync(int userId, CancellationToken cancellationToken = default);
    Task<UsageSummaryDto> GetUsageSummaryAsync(int userId, CancellationToken cancellationToken = default);
}

public interface ISubscriptionService
{
    Task<UserSubscriptionDto> GetUserSubscriptionAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> GetActivePlansAsync(CancellationToken cancellationToken = default);
    Task<UsageSummaryDto> GetUsageAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserSubscriptionDto> AssignPlanAsync(int userId, string planName, CancellationToken cancellationToken = default);
}
