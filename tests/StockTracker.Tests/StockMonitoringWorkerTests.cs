using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Workers;

namespace StockTracker.Tests;

public class StockMonitoringWorkerTests
{
    private readonly Mock<IStockMonitorRepository> _repoMock = new();
    private readonly Mock<IStockCheckerService> _checkerMock = new();
    private readonly Mock<ILogger<StockMonitoringWorker>> _loggerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly IConfiguration _config;

    public StockMonitoringWorkerTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:WorkerIntervalSeconds", "30" }
            })
            .Build();

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);

        _serviceProviderMock.Setup(p => p.GetService(typeof(IStockMonitorRepository)))
            .Returns(_repoMock.Object);
        _serviceProviderMock.Setup(p => p.GetService(typeof(IStockCheckerService)))
            .Returns(_checkerMock.Object);
    }

    [Fact]
    public async Task ProcessDueMonitorsAsync_ProcessesAllDueMonitors()
    {
        var worker = new StockMonitoringWorker(_scopeFactoryMock.Object, _config, _loggerMock.Object);

        var monitor1 = new StockMonitor { Id = 1, ProductName = "Ürün 1", IsActive = true, NextCheckAt = DateTime.UtcNow.AddMinutes(-1) };
        var monitor2 = new StockMonitor { Id = 2, ProductName = "Ürün 2", IsActive = true, NextCheckAt = DateTime.UtcNow.AddMinutes(-5) };

        _repoMock.Setup(r => r.GetDueMonitorsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMonitor> { monitor1, monitor2 });

        _checkerMock.Setup(c => c.CheckMonitorAsync(It.IsAny<StockMonitor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockChange>());

        await worker.ProcessDueMonitorsAsync(CancellationToken.None);

        _checkerMock.Verify(c => c.CheckMonitorAsync(monitor1, It.IsAny<CancellationToken>()), Times.Once);
        _checkerMock.Verify(c => c.CheckMonitorAsync(monitor2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDueMonitorsAsync_WhenOneMonitorFails_ContinuesWithRemainingMonitors()
    {
        var worker = new StockMonitoringWorker(_scopeFactoryMock.Object, _config, _loggerMock.Object);

        var failingMonitor = new StockMonitor { Id = 1, ProductName = "Bozuk Ürün", IsActive = true };
        var successfulMonitor = new StockMonitor { Id = 2, ProductName = "Sağlam Ürün", IsActive = true };

        _repoMock.Setup(r => r.GetDueMonitorsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMonitor> { failingMonitor, successfulMonitor });

        _checkerMock.Setup(c => c.CheckMonitorAsync(failingMonitor, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Scraper connection dropped"));

        _checkerMock.Setup(c => c.CheckMonitorAsync(successfulMonitor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockChange>());

        // Worker loop must handle exception and not crash
        await worker.ProcessDueMonitorsAsync(CancellationToken.None);

        _checkerMock.Verify(c => c.CheckMonitorAsync(failingMonitor, It.IsAny<CancellationToken>()), Times.Once);
        _checkerMock.Verify(c => c.CheckMonitorAsync(successfulMonitor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDueMonitorsAsync_WhenNoMonitorsDue_DoesNotCallChecker()
    {
        var worker = new StockMonitoringWorker(_scopeFactoryMock.Object, _config, _loggerMock.Object);

        _repoMock.Setup(r => r.GetDueMonitorsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMonitor>());

        await worker.ProcessDueMonitorsAsync(CancellationToken.None);

        _checkerMock.Verify(c => c.CheckMonitorAsync(It.IsAny<StockMonitor>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
