using StockTracker.Domain.Entities;

namespace StockTracker.Application.Interfaces;

/// <summary>
/// Abstraction for the application's persistence layer.
/// </summary>
public interface IAppDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
