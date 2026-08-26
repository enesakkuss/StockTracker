using Microsoft.Extensions.Logging;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IUsageLimitService _usageLimitService;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepo,
        IUsageLimitService usageLimitService,
        ILogger<SubscriptionService> logger)
    {
        _subscriptionRepo = subscriptionRepo;
        _usageLimitService = usageLimitService;
        _logger = logger;
    }

    public async Task<UserSubscriptionDto> GetUserSubscriptionAsync(int userId, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepo.GetActiveSubscriptionByUserIdAsync(userId, cancellationToken);
        if (subscription != null && subscription.Plan != null)
        {
            var plan = subscription.Plan;
            var limits = new PlanLimitsDto(
                plan.MaxActiveMonitors,
                plan.MaxTotalMonitors,
                plan.MinCheckIntervalMinutes,
                plan.TelegramEnabled,
                plan.MaxNotificationsPerDay,
                plan.MaxInspectRequestsPerDay
            );

            return new UserSubscriptionDto(
                subscription.Id,
                userId,
                plan.Name,
                subscription.Status.ToString(),
                subscription.StartedAt,
                subscription.ExpiresAt,
                limits
            );
        }

        // Default FREE plan
        var freePlan = await _subscriptionRepo.GetPlanByNameAsync("FREE", cancellationToken);
        var freeLimits = new PlanLimitsDto(
            freePlan?.MaxActiveMonitors ?? 5,
            freePlan?.MaxTotalMonitors ?? 10,
            freePlan?.MinCheckIntervalMinutes ?? 60,
            freePlan?.TelegramEnabled ?? true,
            freePlan?.MaxNotificationsPerDay ?? 20,
            freePlan?.MaxInspectRequestsPerDay ?? 20
        );

        return new UserSubscriptionDto(
            0,
            userId,
            "FREE",
            "Active",
            DateTime.UtcNow,
            null,
            freeLimits
        );
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetActivePlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionRepo.GetActivePlansAsync(cancellationToken);
        return plans.Select(p => new SubscriptionPlanDto(
            p.Id,
            p.Name,
            p.Description,
            p.Price,
            p.Currency,
            p.BillingPeriod,
            new PlanLimitsDto(
                p.MaxActiveMonitors,
                p.MaxTotalMonitors,
                p.MinCheckIntervalMinutes,
                p.TelegramEnabled,
                p.MaxNotificationsPerDay,
                p.MaxInspectRequestsPerDay
            ),
            p.IsActive
        )).ToList();
    }

    public async Task<UsageSummaryDto> GetUsageAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _usageLimitService.GetUsageSummaryAsync(userId, cancellationToken);
    }

    public async Task<UserSubscriptionDto> AssignPlanAsync(int userId, string planName, CancellationToken cancellationToken = default)
    {
        var plan = await _subscriptionRepo.GetPlanByNameAsync(planName, cancellationToken);
        if (plan is null || !plan.IsActive)
        {
            throw new KeyNotFoundException($"Plan bulunamadı veya aktif değil: {planName}");
        }

        var existing = await _subscriptionRepo.GetActiveSubscriptionByUserIdAsync(userId, cancellationToken);
        if (existing != null)
        {
            existing.Status = SubscriptionStatus.Cancelled;
            existing.CancelledAt = DateTime.UtcNow;
            await _subscriptionRepo.UpdateSubscriptionAsync(existing, cancellationToken);
        }

        var newSub = new Subscription
        {
            UserId = userId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = plan.Name == "FREE" ? null : DateTime.UtcNow.AddMonths(1)
        };

        var created = await _subscriptionRepo.AddSubscriptionAsync(newSub, cancellationToken);
        _logger.LogInformation("Assigned plan {PlanName} to user {UserId}", plan.Name, userId);

        var limits = new PlanLimitsDto(
            plan.MaxActiveMonitors,
            plan.MaxTotalMonitors,
            plan.MinCheckIntervalMinutes,
            plan.TelegramEnabled,
            plan.MaxNotificationsPerDay,
            plan.MaxInspectRequestsPerDay
        );

        return new UserSubscriptionDto(
            created.Id,
            userId,
            plan.Name,
            created.Status.ToString(),
            created.StartedAt,
            created.ExpiresAt,
            limits
        );
    }
}
