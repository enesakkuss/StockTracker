using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using StockTracker.Api.Middleware;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;
using StockTracker.Infrastructure;
using StockTracker.Infrastructure.Persistence;

var webAppOptions = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
};
var builder = WebApplication.CreateBuilder(webAppOptions);

// Kestrel Hardening
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB request size limit
    options.AddServerHeader = false; // Never advertise server info
});

// Controllers + API Explorer
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS configuration (Configurable via Cors:AllowedOrigins with secure localhost default)
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(origin =>
                {
                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                    }
                    return false;
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global IP partition limiter
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 500,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // Stricter policy for Auth endpoints to prevent brute force
    options.AddFixedWindowLimiter("AuthRateLimit", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Policy for Product Inspect
    options.AddFixedWindowLimiter("InspectRateLimit", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<StockTracker.Infrastructure.Services.DatabaseHealthCheck>("database", tags: new[] { "ready" });

// Swagger / OpenAPI with JWT Bearer support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StockTracker Multi-User API",
        Version = "v1",
        Description = "Çok kullanıcılı ürün stok takip SaaS uygulaması API'si"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 12345abcdef\""
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// Infrastructure (EF Core, adapters, auth, services)
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Forwarded Headers for reverse proxy (Nginx, Caddy, Cloudflare)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

// Security Headers Middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Global Exception and Correlation ID Middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

// Database Migration and Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    if (!db.Users.Any())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var defaultUser = new User
        {
            Email = "admin@stocktracker.local",
            PasswordHash = hasher.HashPassword("Admin123456!"),
            FirstName = "Default",
            LastName = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(defaultUser);
        db.SaveChanges();

        var existingMonitors = db.StockMonitors.Where(m => m.UserId == 0).ToList();
        foreach (var m in existingMonitors)
        {
            m.UserId = defaultUser.Id;
        }
        if (existingMonitors.Count > 0)
        {
            db.SaveChanges();
        }
    }

    if (!db.SubscriptionPlans.Any())
    {
        var freePlan = new SubscriptionPlan
        {
            Name = "FREE",
            Description = "Temel ürün takip planı",
            Price = 0.00m,
            Currency = "TRY",
            BillingPeriod = "Monthly",
            MaxActiveMonitors = 5,
            MaxTotalMonitors = 10,
            MinCheckIntervalMinutes = 60,
            TelegramEnabled = true,
            MaxNotificationsPerDay = 20,
            MaxInspectRequestsPerDay = 20,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow
        };

        var premiumPlan = new SubscriptionPlan
        {
            Name = "PREMIUM",
            Description = "Sık aralıklı profesyonel stok takip planı",
            Price = 199.00m,
            Currency = "TRY",
            BillingPeriod = "Monthly",
            MaxActiveMonitors = 100,
            MaxTotalMonitors = 500,
            MinCheckIntervalMinutes = 5,
            TelegramEnabled = true,
            MaxNotificationsPerDay = 1000,
            MaxInspectRequestsPerDay = 500,
            IsActive = true,
            SortOrder = 2,
            CreatedAt = DateTime.UtcNow
        };

        db.SubscriptionPlans.AddRange(freePlan, premiumPlan);
        db.SaveChanges();
    }
}

// Swagger (Enabled in Development or explicitly via configuration)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "StockTracker API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("DefaultCorsPolicy");
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && (path.EndsWith(".css") || path.EndsWith(".js") || path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".svg") || path.EndsWith(".woff2")))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
        }
        else
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        }
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Production Health Check Response Writer (Safe, no internal secrets leaked)
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var result = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
        entries = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 2)
        })
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    return context.Response.WriteAsync(result);
}

// Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // liveness responds 200 if process is up
    ResponseWriter = WriteHealthResponse
});

app.MapControllers();

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
