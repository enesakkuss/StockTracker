using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<StockMonitor> StockMonitors => Set<StockMonitor>();
    public DbSet<StockMonitorVariantState> StockMonitorVariantStates => Set<StockMonitorVariantState>();
    public DbSet<StockNotificationHistory> StockNotificationHistories => Set<StockNotificationHistory>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<DailyUsageRecord> DailyUsageRecords => Set<DailyUsageRecord>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<TelegramSettings> TelegramSettings => Set<TelegramSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(255);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.LastName).IsRequired().HasMaxLength(100);

            entity.HasMany(u => u.RefreshTokens)
                  .WithOne(r => r.User)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.Subscriptions)
                  .WithOne(s => s.User)
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.DailyUsageRecords)
                  .WithOne(d => d.User)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.PaymentTransactions)
                  .WithOne(t => t.User)
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.Monitors)
                  .WithOne(m => m.User)
                  .HasForeignKey(m => m.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.NotificationHistories)
                  .WithOne(h => h.User)
                  .HasForeignKey(h => h.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.TokenHash).IsRequired().HasMaxLength(255);
            entity.HasIndex(r => r.TokenHash);
            entity.HasIndex(r => r.UserId);
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(50);
            entity.HasIndex(p => p.Name).IsUnique();
            entity.Property(p => p.Description).HasMaxLength(500);
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Currency).IsRequired().HasMaxLength(10);
            entity.Property(p => p.BillingPeriod).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.Status);
            entity.Property(s => s.PaymentProvider).HasMaxLength(50);
            entity.Property(s => s.ExternalSubscriptionId).HasMaxLength(100);

            entity.HasOne(s => s.Plan)
                  .WithMany(p => p.Subscriptions)
                  .HasForeignKey(s => s.PlanId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(s => s.PaymentTransactions)
                  .WithOne(t => t.Subscription)
                  .HasForeignKey(t => t.SubscriptionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Amount).HasPrecision(18, 2);
            entity.Property(t => t.Currency).IsRequired().HasMaxLength(10);
            entity.Property(t => t.Provider).IsRequired().HasMaxLength(50);
            entity.Property(t => t.ProviderTransactionId).IsRequired().HasMaxLength(100);
            entity.Property(t => t.IdempotencyKey).HasMaxLength(100);

            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.SubscriptionId);
            entity.HasIndex(t => new { t.Provider, t.ProviderTransactionId });
            entity.HasIndex(t => new { t.UserId, t.IdempotencyKey });
        });

        modelBuilder.Entity<PaymentWebhookEvent>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Provider).IsRequired().HasMaxLength(50);
            entity.Property(w => w.EventId).IsRequired().HasMaxLength(100);
            entity.Property(w => w.EventType).IsRequired().HasMaxLength(100);
            entity.Property(w => w.PayloadHash).HasMaxLength(255);

            entity.HasIndex(w => new { w.Provider, w.EventId }).IsUnique();
        });

        modelBuilder.Entity<DailyUsageRecord>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.DateKey).IsRequired().HasMaxLength(15);
            entity.HasIndex(d => new { d.UserId, d.DateKey }).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Url).IsRequired();
            entity.Property(p => p.Name).IsRequired();
            entity.Property(p => p.StoreType).IsRequired();

            entity.HasMany(p => p.Variants)
                  .WithOne(v => v.Product)
                  .HasForeignKey(v => v.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Size).IsRequired();
        });

        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList()
        );

        modelBuilder.Entity<StockMonitor>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.UserId);
            entity.Property(m => m.ProductUrl).IsRequired();
            entity.Property(m => m.Store).IsRequired();
            entity.Property(m => m.ProductName).IsRequired();
            entity.Property(m => m.ProtectedTelegramBotToken).IsRequired();
            entity.Property(m => m.TelegramChatId).IsRequired();

            entity.Property(m => m.SelectedVariants)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => string.IsNullOrEmpty(v)
                          ? new List<string>()
                          : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                  )
                  .Metadata.SetValueComparer(stringListComparer);

            entity.HasMany(m => m.VariantStates)
                  .WithOne(s => s.StockMonitor)
                  .HasForeignKey(s => s.StockMonitorId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(m => m.NotificationHistories)
                  .WithOne(h => h.StockMonitor)
                  .HasForeignKey(h => h.StockMonitorId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockMonitorVariantState>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.VariantName).IsRequired();
            entity.HasIndex(s => new { s.StockMonitorId, s.VariantName });
        });

        modelBuilder.Entity<StockNotificationHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.UserId);
            entity.Property(h => h.VariantName).IsRequired();
            entity.HasIndex(h => new { h.StockMonitorId, h.VariantName, h.StockChangeAt });
        });

        modelBuilder.Entity<TelegramSettings>(entity =>
        {
            entity.HasKey(t => t.Id);
        });
    }
}
