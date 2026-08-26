using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StockTracker.Api.Middleware;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure.Persistence;
using StockTracker.Infrastructure.Services;
using StockTracker.Infrastructure.Workers;
using Xunit;

namespace StockTracker.Tests;

public class ProductionDeploymentAndInfrastructureTests
{
    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "StockTracker.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return dir ?? Path.GetFullPath(".");
    }

    [Fact]
    public void ProductionConfig_Json_IsValidAndContainsRequiredSections()
    {
        var rootDir = GetSolutionRoot();
        var prodConfigPath = Path.Combine(rootDir, "src", "StockTracker.Api", "appsettings.Production.json");

        Assert.True(File.Exists(prodConfigPath), $"appsettings.Production.json not found at {prodConfigPath}");

        var jsonContent = File.ReadAllText(prodConfigPath);
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("Logging", out _));
        Assert.True(root.TryGetProperty("ConnectionStrings", out var connSection));
        Assert.True(connSection.TryGetProperty("DefaultConnection", out _));
        Assert.True(root.TryGetProperty("StockMonitoring", out _));
        Assert.True(root.TryGetProperty("Jwt", out _));
        Assert.True(root.TryGetProperty("Browser", out _));
    }

    [Fact]
    public void DataProtection_EncryptionAndDecryption_IsDeterministicAcrossRestarts()
    {
        var config1 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Security:SecretProtectionKey", "Persistent_Production_Secret_Key_2026_ABCD_1234!" }
            })
            .Build();

        var protector1 = new DataProtectionSecretProtector(config1);
        var rawSecret = "987654321:AA_PROD_PERSISTENT_BOT_TOKEN";
        var encrypted = protector1.Protect(rawSecret);

        Assert.False(string.IsNullOrWhiteSpace(encrypted));
        Assert.NotEqual(rawSecret, encrypted);

        // Simulate server restart / new instance with same persistent key
        var config2 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Security:SecretProtectionKey", "Persistent_Production_Secret_Key_2026_ABCD_1234!" }
            })
            .Build();

        var protector2 = new DataProtectionSecretProtector(config2);
        var decrypted = protector2.Unprotect(encrypted);

        Assert.Equal(rawSecret, decrypted);
    }

    [Fact]
    public async Task Worker_GracefulShutdown_HonorsCancellationToken()
    {
        var scopeFactoryMock = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "StockMonitoring:WorkerIntervalSeconds", "5" }
            })
            .Build();

        var loggerMock = new Mock<ILogger<StockMonitoringWorker>>();
        var worker = new StockMonitoringWorker(scopeFactoryMock.Object, config, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        // Cancel quickly to simulate server shutdown (SIGTERM)
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(workerTask.IsCompleted);
    }

    [Fact]
    public void Dockerfile_And_Compose_ExistAndContainRequiredConfiguration()
    {
        var baseDir = GetSolutionRoot();
        var dockerfilePath = Path.Combine(baseDir, "Dockerfile");
        var composePath = Path.Combine(baseDir, "docker-compose.prod.yml");
        var nginxPath = Path.Combine(baseDir, "nginx.prod.conf");

        Assert.True(File.Exists(dockerfilePath), $"Dockerfile must exist at {dockerfilePath}");
        Assert.True(File.Exists(composePath), $"docker-compose.prod.yml must exist at {composePath}");
        Assert.True(File.Exists(nginxPath), $"nginx.prod.conf must exist at {nginxPath}");

        var dockerfileContent = File.ReadAllText(dockerfilePath);
        Assert.Contains("mcr.microsoft.com/dotnet/aspnet:8.0", dockerfileContent);
        Assert.Contains("/health/live", dockerfileContent);

        var composeContent = File.ReadAllText(composePath);
        Assert.Contains("stocktracker-data", composeContent);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", composeContent);
    }

    [Fact]
    public void DeploymentScripts_ExistAndAreConfigured()
    {
        var baseDir = GetSolutionRoot();
        var deploySh = Path.Combine(baseDir, "scripts", "deploy.sh");
        var backupSh = Path.Combine(baseDir, "scripts", "backup.sh");
        var restoreSh = Path.Combine(baseDir, "scripts", "restore.sh");

        Assert.True(File.Exists(deploySh), $"scripts/deploy.sh must exist at {deploySh}");
        Assert.True(File.Exists(backupSh), $"scripts/backup.sh must exist at {backupSh}");
        Assert.True(File.Exists(restoreSh), $"scripts/restore.sh must exist at {restoreSh}");
    }

    [Fact]
    public void Documentation_AllFiveProductionGuides_ExistInDocs()
    {
        var baseDir = GetSolutionRoot();
        var docs = new[]
        {
            Path.Combine(baseDir, "docs", "DEPLOYMENT.md"),
            Path.Combine(baseDir, "docs", "PRODUCTION-CHECKLIST.md"),
            Path.Combine(baseDir, "docs", "SECURITY.md"),
            Path.Combine(baseDir, "docs", "INFRASTRUCTURE.md"),
            Path.Combine(baseDir, "docs", "BACKUP-RESTORE.md"),
            Path.Combine(baseDir, "docs", "ROLLBACK.md")
        };

        foreach (var doc in docs)
        {
            Assert.True(File.Exists(doc), $"Documentation file {Path.GetFileName(doc)} must exist in {doc}");
        }
    }
}
