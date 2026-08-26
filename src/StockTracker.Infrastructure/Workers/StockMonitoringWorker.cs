using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockTracker.Application.Interfaces;

namespace StockTracker.Infrastructure.Workers;

/// <summary>
/// Background worker that periodically checks active stock monitors whose check interval has arrived.
/// Operates asynchronously without blocking web requests, handles per-monitor failures safely,
/// and detects stock changes using store adapters.
/// </summary>
public class StockMonitoringWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockMonitoringWorker> _logger;
    private readonly int _workerIntervalSeconds;

    public StockMonitoringWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StockMonitoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        if (!int.TryParse(configuration["StockMonitoring:WorkerIntervalSeconds"], out _workerIntervalSeconds) || _workerIntervalSeconds < 5)
        {
            _workerIntervalSeconds = 30;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StockMonitoringWorker started with polling interval: {Seconds}s", _workerIntervalSeconds);

        // Initial brief delay to allow server initialization
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueMonitorsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in StockMonitoringWorker loop.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_workerIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("StockMonitoringWorker is stopping.");
    }

    public async Task ProcessDueMonitorsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStockMonitorRepository>();
        var checkerService = scope.ServiceProvider.GetRequiredService<IStockCheckerService>();

        var now = DateTime.UtcNow;
        var dueMonitors = await repository.GetDueMonitorsAsync(now, cancellationToken);

        if (dueMonitors.Count == 0)
        {
            _logger.LogDebug("No due stock monitors found for check at {Time}", now);
            return;
        }

        _logger.LogInformation("Processing {Count} due stock monitor(s)...", dueMonitors.Count);

        foreach (var monitor in dueMonitors)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var changes = await checkerService.CheckMonitorAsync(monitor, cancellationToken);
                if (changes.Count > 0)
                {
                    _logger.LogInformation(
                        "Monitor {Id} ({Product}) detected {Count} stock status change(s).",
                        monitor.Id, monitor.ProductName, changes.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing monitor ID: {Id}. Other monitors will continue.", monitor.Id);
            }
        }
    }
}
