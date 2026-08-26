using Microsoft.EntityFrameworkCore;
using StockTracker.Application.Common;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Persistence;

public class PaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly AppDbContext _context;

    public PaymentTransactionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentTransaction> AddTransactionAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _context.PaymentTransactions.AddAsync(transaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<PaymentTransaction?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.PaymentTransactions
            .Include(t => t.User)
            .Include(t => t.Subscription)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<PaymentTransaction?> GetByProviderTransactionIdAsync(string provider, string providerTransactionId, CancellationToken cancellationToken = default)
    {
        var normProvider = provider.Trim().ToLowerInvariant();
        return await _context.PaymentTransactions
            .Include(t => t.User)
            .Include(t => t.Subscription)
            .FirstOrDefaultAsync(t => t.Provider.ToLower() == normProvider && t.ProviderTransactionId == providerTransactionId, cancellationToken);
    }

    public async Task<PaymentTransaction?> GetByIdempotencyKeyAsync(int userId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await _context.PaymentTransactions
            .Include(t => t.User)
            .Include(t => t.Subscription)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<PagedResponse<PaymentTransaction>> GetPagedByUserIdAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

        var query = _context.PaymentTransactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PagedResponse<PaymentTransaction>(items, totalCount, safePage, safePageSize);
    }

    public async Task UpdateTransactionAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.PaymentTransactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasWebhookBeenProcessedAsync(string provider, string eventId, CancellationToken cancellationToken = default)
    {
        var normProvider = provider.Trim().ToLowerInvariant();
        return await _context.PaymentWebhookEvents
            .AnyAsync(w => w.Provider.ToLower() == normProvider && w.EventId == eventId, cancellationToken);
    }

    public async Task AddWebhookEventAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        await _context.PaymentWebhookEvents.AddAsync(webhookEvent, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
