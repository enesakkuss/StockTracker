using Microsoft.EntityFrameworkCore;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Persistence;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly AppDbContext _context;

    public SubscriptionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetActivePlansAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionPlan?> GetPlanByIdAsync(int planId, CancellationToken cancellationToken = default)
    {
        return await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
    }

    public async Task<SubscriptionPlan?> GetPlanByNameAsync(string planName, CancellationToken cancellationToken = default)
    {
        var normalized = planName.Trim().ToUpperInvariant();
        return await _context.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Name.ToUpper() == normalized, cancellationToken);
    }

    public async Task<Subscription?> GetActiveSubscriptionByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Subscription> AddSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        await _context.Subscriptions.AddAsync(subscription, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    public async Task UpdateSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        subscription.UpdatedAt = DateTime.UtcNow;
        _context.Subscriptions.Update(subscription);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DailyUsageRecord> GetOrCreateDailyUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default)
    {
        var record = await _context.DailyUsageRecords
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DateKey == dateKey, cancellationToken);

        if (record is null)
        {
            record = new DailyUsageRecord
            {
                UserId = userId,
                DateKey = dateKey,
                InspectRequestsCount = 0,
                NotificationsCount = 0,
                LastActivityAt = DateTime.UtcNow
            };
            await _context.DailyUsageRecords.AddAsync(record, cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Concurrency catch: if another request created it simultaneously
                record = await _context.DailyUsageRecords
                    .FirstAsync(d => d.UserId == userId && d.DateKey == dateKey, cancellationToken);
            }
        }

        return record;
    }

    public async Task IncrementInspectUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateDailyUsageAsync(userId, dateKey, cancellationToken);
        record.InspectRequestsCount++;
        record.LastActivityAt = DateTime.UtcNow;
        _context.DailyUsageRecords.Update(record);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task IncrementNotificationUsageAsync(int userId, string dateKey, CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateDailyUsageAsync(userId, dateKey, cancellationToken);
        record.NotificationsCount++;
        record.LastActivityAt = DateTime.UtcNow;
        _context.DailyUsageRecords.Update(record);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
