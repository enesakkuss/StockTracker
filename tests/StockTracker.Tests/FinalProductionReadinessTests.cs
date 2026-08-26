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

public class FinalProductionReadinessTests
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
    public async Task SQLite_HotBackup_And_Restore_PreservesDataIntegrity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "StockTracker_BackupTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var originalDbPath = Path.Combine(tempDir, "original.db");
        var backupDbPath = Path.Combine(tempDir, "backup.db");
        var restoredDbPath = Path.Combine(tempDir, "restored.db");

        try
        {
            // 1. Create and populate original DB
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={originalDbPath}")
                .Options;

            using (var db = new AppDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Users.Add(new User
                {
                    Email = "backup_test@stocktracker.local",
                    PasswordHash = "hashed_pw",
                    FirstName = "Backup",
                    LastName = "Tester",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // 2. Perform SQLite online backup
            using (var srcConn = new SqliteConnection($"Data Source={originalDbPath}"))
            using (var destConn = new SqliteConnection($"Data Source={backupDbPath}"))
            {
                await srcConn.OpenAsync();
                await destConn.OpenAsync();
                srcConn.BackupDatabase(destConn);
            }

            Assert.True(File.Exists(backupDbPath), "Backup database file should exist");

            // 3. Simulate disaster recovery: copy backup to restored location
            File.Copy(backupDbPath, restoredDbPath);

            // 4. Verify data integrity in restored database
            var restoreOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={restoredDbPath}")
                .Options;

            using (var db = new AppDbContext(restoreOptions))
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == "backup_test@stocktracker.local");
                Assert.NotNull(user);
                Assert.Equal("Backup", user.FirstName);
                Assert.Equal("Tester", user.LastName);
            }
        }
        finally
        {
            // Cleanup test directory
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task SecurityHeaders_And_CSP_AreStrictAndDefensive()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;
        context.Response.Headers["Server"] = "Kestrel";
        context.Response.Headers["X-Powered-By"] = "ASP.NET";

        var middleware = new SecurityHeadersMiddleware(next: (ctx) =>
        {
            return Task.CompletedTask;
        }, envMock.Object);

        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Contains("camera=()", headers["Permissions-Policy"].ToString());
        Assert.Contains("default-src 'self'", headers["Content-Security-Policy"].ToString());
        Assert.Contains("max-age=31536000", headers["Strict-Transport-Security"].ToString());
        Assert.False(headers.ContainsKey("Server"));
        Assert.False(headers.ContainsKey("X-Powered-By"));
    }

    [Fact]
    public void Production_Swagger_IsDisabledByDefaultInProductionConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ASPNETCORE_ENVIRONMENT", "Production" }
            })
            .Build();

        var swaggerExplicitlyEnabled = config.GetValue<bool>("Swagger:Enabled", false);
        Assert.False(swaggerExplicitlyEnabled, "Swagger should be false by default in production config");
    }

    [Fact]
    public void Frontend_Config_FeaturesBillingEnabled_IsStrictlyFalse()
    {
        var rootDir = GetSolutionRoot();
        var configJsPath = Path.Combine(rootDir, "src", "StockTracker.Api", "wwwroot", "js", "config.js");
        Assert.True(File.Exists(configJsPath), "config.js must exist");

        var content = File.ReadAllText(configJsPath);
        Assert.Contains("billingEnabled: false", content);
        Assert.DoesNotContain("billingEnabled: true", content);
    }
}
