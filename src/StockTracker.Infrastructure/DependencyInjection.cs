using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StockTracker.Application.Interfaces;
using StockTracker.Application.Services;
using StockTracker.Infrastructure.Adapters;
using StockTracker.Infrastructure.Configuration;
using StockTracker.Infrastructure.Persistence;
using StockTracker.Infrastructure.Services;
using StockTracker.Infrastructure.Workers;

namespace StockTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // HTTP Client Factory for external API calls (Telegram, etc.)
        services.AddHttpClient();

        // SQLite + EF Core
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=stocktracker.db";

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // User and Auth Repositories & Services
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserTelegramService, UserTelegramService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IUsageLimitService, UsageLimitService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        // Payment Options & Repositories
        services.Configure<StockTracker.Infrastructure.Payments.Iyzico.IyzicoOptions>(
            configuration.GetSection(StockTracker.Infrastructure.Payments.Iyzico.IyzicoOptions.SectionName));

        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddScoped<IPaymentProvider, StockTracker.Infrastructure.Payments.Mock.MockPaymentProvider>();
        services.AddScoped<IPaymentProvider, StockTracker.Infrastructure.Payments.Iyzico.IyzicoPaymentService>();
        services.AddScoped<IPaymentService, PaymentService>();

        // JWT Authentication Setup
        var secretKey = configuration["Jwt:SecretKey"]
            ?? configuration["JWT_SECRET_KEY"]
            ?? "StockTracker_Development_Super_Secret_Key_At_Least_32_Bytes_Long_2026";
        var issuer = configuration["Jwt:Issuer"] ?? "StockTracker";
        var audience = configuration["Jwt:Audience"] ?? "StockTrackerUsers";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

        services.AddAuthorization();

        // Secret protection
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Browser configuration
        services.Configure<BrowserOptions>(options =>
            configuration.GetSection(BrowserOptions.SectionName).Bind(options));

        // Playwright browser — singleton so it's created once and reused
        services.AddSingleton<IBrowserService, PlaywrightBrowserService>();

        // Universal Store Adapters — all 13 supported stores
        services.AddScoped<IStoreAdapter, ZaraAdapter>();
        services.AddScoped<IStoreAdapter, MangoAdapter>();
        services.AddScoped<IStoreAdapter, PullAndBearAdapter>();
        services.AddScoped<IStoreAdapter, BershkaAdapter>();
        services.AddScoped<IStoreAdapter, StradivariusAdapter>();
        services.AddScoped<IStoreAdapter, MassimoDuttiAdapter>();
        services.AddScoped<IStoreAdapter, OyshoAdapter>();
        services.AddScoped<IStoreAdapter, MaviAdapter>();
        services.AddScoped<IStoreAdapter, HmAdapter>();
        services.AddScoped<IStoreAdapter, KotonAdapter>();
        services.AddScoped<IStoreAdapter, LcWaikikiAdapter>();
        services.AddScoped<IStoreAdapter, DefactoAdapter>();
        services.AddScoped<IStoreAdapter, PentiAdapter>();

        // Universal Store Adapter Registry & Resolver
        services.AddScoped<StoreAdapterRegistry>();
        services.AddScoped<IStoreAdapterRegistry>(sp => sp.GetRequiredService<StoreAdapterRegistry>());
        services.AddScoped<IStoreAdapterResolver>(sp => sp.GetRequiredService<StoreAdapterRegistry>());

        // Stock Monitor persistence, checking & business service
        services.AddScoped<IStockMonitorRepository, StockMonitorRepository>();
        services.AddScoped<IStockMonitorService, StockMonitorService>();
        services.AddScoped<IStockCheckerService, StockCheckerService>();

        // Application services
        services.AddScoped<ProductService>();
        services.AddScoped<ProductInspectService>();

        // Background worker for periodic stock checks
        services.AddHostedService<StockMonitoringWorker>();

        // Telegram & Notification service
        services.AddScoped<TelegramNotificationService>();
        services.AddScoped<ITelegramService>(sp => sp.GetRequiredService<TelegramNotificationService>());
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<TelegramNotificationService>());

        return services;
    }
}
