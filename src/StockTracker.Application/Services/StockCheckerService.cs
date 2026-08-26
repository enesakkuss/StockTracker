using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Services;

public class StockCheckerService : IStockCheckerService
{
    private readonly IStoreAdapterResolver _adapterResolver;
    private readonly IStockMonitorRepository _repository;
    private readonly INotificationService _notificationService;
    private readonly IUsageLimitService? _usageLimitService;
    private readonly ILogger<StockCheckerService> _logger;
    private readonly int _requestTimeoutSeconds;

    public StockCheckerService(
        IStoreAdapterResolver adapterResolver,
        IStockMonitorRepository repository,
        INotificationService notificationService,
        IConfiguration configuration,
        ILogger<StockCheckerService> logger,
        IUsageLimitService? usageLimitService = null)
    {
        _adapterResolver = adapterResolver;
        _repository = repository;
        _notificationService = notificationService;
        _usageLimitService = usageLimitService;
        _logger = logger;

        if (!int.TryParse(configuration["StockMonitoring:RequestTimeoutSeconds"], out _requestTimeoutSeconds) || _requestTimeoutSeconds < 5)
        {
            _requestTimeoutSeconds = 30;
        }
    }

    public async Task<IReadOnlyList<StockChange>> CheckMonitorAsync(StockMonitor monitor, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var changes = new List<StockChange>();

        // Find appropriate adapter
        var adapter = _adapterResolver.Resolve(monitor.ProductUrl);
        if (adapter is null)
        {
            _logger.LogWarning("No store adapter found for monitor ID {Id} (URL: {Url})", monitor.Id, monitor.ProductUrl);
            monitor.LastCheckedAt = now;
            monitor.LastCheckStatus = "Failed";
            monitor.LastCheckError = "Ürünün mağazası için uygun adapter bulunamadı.";
            monitor.NextCheckAt = now.AddMinutes(Math.Min(5, monitor.CheckIntervalMinutes));
            await _repository.UpdateAsync(monitor, cancellationToken);
            return changes;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_requestTimeoutSeconds));

        try
        {
            _logger.LogInformation("Checking stock for monitor ID: {Id} ({Product})", monitor.Id, monitor.ProductName);

            IReadOnlyList<VariantAvailabilityDto> freshVariants;

            // Preferred: rich inspect contract
            if (adapter is IInspectableAdapter inspectable)
            {
                var inspectRes = await inspectable.InspectAsync(monitor.ProductUrl, timeoutCts.Token);
                freshVariants = inspectRes.Variants;

                if (!string.IsNullOrWhiteSpace(inspectRes.ImageUrl) && monitor.ImageUrl != inspectRes.ImageUrl)
                {
                    monitor.ImageUrl = inspectRes.ImageUrl;
                }
                if (!string.IsNullOrWhiteSpace(inspectRes.Name) && monitor.ProductName != inspectRes.Name)
                {
                    monitor.ProductName = inspectRes.Name;
                }
            }
            else
            {
                var product = await adapter.FetchProductAsync(monitor.ProductUrl, timeoutCts.Token);
                if (product is null)
                {
                    throw new InvalidOperationException($"Mağazadan ürün bilgisi çekilemedi ({monitor.Store}).");
                }

                freshVariants = product.Variants
                    .Select(v => new VariantAvailabilityDto(v.Size, v.IsInStock))
                    .ToList();

                if (!string.IsNullOrWhiteSpace(product.ImageUrl) && monitor.ImageUrl != product.ImageUrl)
                {
                    monitor.ImageUrl = product.ImageUrl;
                }
                if (!string.IsNullOrWhiteSpace(product.Name) && monitor.ProductName != product.Name)
                {
                    monitor.ProductName = product.Name;
                }
            }

            // If no variants extracted, Zero Fake Data: do not assume false, log warning
            if (freshVariants.Count == 0)
            {
                _logger.LogWarning("Monitor {Id}: No variants extracted from store {Store}", monitor.Id, monitor.Store);
                monitor.LastCheckedAt = now;
                monitor.NextCheckAt = now.AddMinutes(monitor.CheckIntervalMinutes);
                monitor.LastCheckStatus = "Incomplete";
                monitor.LastCheckError = "Beden stok bilgisi sayfada bulunamadı.";
                await _repository.UpdateAsync(monitor, cancellationToken);
                return changes;
            }

            // Compare variants for each tracked size
            var selectedVariants = monitor.SelectedVariants;
            foreach (var selectedVariant in selectedVariants)
            {
                var matched = freshVariants.FirstOrDefault(v => VariantMatcher.IsMatch(selectedVariant, v.Name));
                if (matched is null)
                {
                    _logger.LogWarning("Monitor {Id}: Tracked variant '{Variant}' not found in fetched variant list", monitor.Id, selectedVariant);
                    continue;
                }

                var existingState = monitor.VariantStates.FirstOrDefault(s =>
                    string.Equals(s.VariantName, selectedVariant, StringComparison.OrdinalIgnoreCase));

                var currentAvailable = matched.Available;

                if (existingState is null)
                {
                    // Baseline state insertion (initial check)
                    var newState = new StockMonitorVariantState
                    {
                        StockMonitorId = monitor.Id,
                        VariantName = selectedVariant,
                        IsAvailable = currentAvailable,
                        LastCheckedAt = now,
                        LastChangedAt = now
                    };
                    monitor.VariantStates.Add(newState);
                }
                else
                {
                    var previous = existingState.IsAvailable;
                    if (previous != currentAvailable)
                    {
                        existingState.IsAvailable = currentAvailable;
                        existingState.LastCheckedAt = now;
                        existingState.LastChangedAt = now;

                        var change = new StockChange(
                            MonitorId: monitor.Id,
                            ProductName: monitor.ProductName,
                            Store: monitor.Store,
                            ProductUrl: monitor.ProductUrl,
                            VariantName: selectedVariant,
                            PreviousAvailability: previous,
                            CurrentAvailability: currentAvailable,
                            ChangedAt: now,
                            IsInitialCheck: false
                        );
                        changes.Add(change);

                        _logger.LogInformation(
                            "Monitor {Id}: Stock status changed for '{Variant}' ({Prev} -> {Curr})",
                            monitor.Id, selectedVariant, previous, currentAvailable);

                        // ── Automatic Telegram Notification (Only on false -> true) ──
                        if (previous == false && currentAvailable == true)
                        {
                            await ProcessStockArrivalNotificationAsync(monitor, change, cancellationToken);
                        }
                    }
                    else
                    {
                        existingState.LastCheckedAt = now;
                    }
                }
            }

            // Success update
            monitor.LastCheckedAt = now;
            monitor.NextCheckAt = now.AddMinutes(monitor.CheckIntervalMinutes);
            monitor.LastCheckStatus = "Success";
            monitor.LastCheckError = null;

            await _repository.UpdateAsync(monitor, cancellationToken);
            _logger.LogInformation("Monitor {Id} checked successfully. {Count} changes detected.", monitor.Id, changes.Count);

            return changes;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Stock check timed out after {Timeout}s for monitor ID: {Id}", _requestTimeoutSeconds, monitor.Id);
            monitor.LastCheckedAt = now;
            monitor.NextCheckAt = now.AddMinutes(Math.Min(5, monitor.CheckIntervalMinutes));
            monitor.LastCheckStatus = "Failed";
            monitor.LastCheckError = "Ürün sayfası kontrol edilirken zaman aşımına uğrandı.";
            await _repository.UpdateAsync(monitor, cancellationToken);
            return changes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stock check failed for monitor ID: {Id}", monitor.Id);
            monitor.LastCheckedAt = now;
            monitor.NextCheckAt = now.AddMinutes(Math.Min(5, monitor.CheckIntervalMinutes));
            monitor.LastCheckStatus = "Failed";
            monitor.LastCheckError = SanitizeError(ex.Message);
            await _repository.UpdateAsync(monitor, cancellationToken);
            return changes;
        }
    }

    public async Task<ManualCheckResponse> CheckMonitorByIdAsync(int monitorId, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdWithStatesAsync(monitorId, cancellationToken);
        if (monitor is null)
        {
            throw new KeyNotFoundException($"ID {monitorId} olan stok takibi bulunamadı.");
        }

        if (!monitor.IsActive)
        {
            throw new InvalidOperationException("Durdurulmuş bir takip kontrol edilemez. Lütfen önce takibi başlatın.");
        }

        var changes = await CheckMonitorAsync(monitor, cancellationToken);

        var notificationsCount = changes.Count(c => c.PreviousAvailability == false && c.CurrentAvailability == true && !c.IsInitialCheck);

        return new ManualCheckResponse(
            MonitorId: monitor.Id,
            ProductName: monitor.ProductName,
            Store: monitor.Store,
            Status: monitor.LastCheckStatus ?? "Completed",
            Changes: changes,
            Error: monitor.LastCheckError,
            NotificationsSent: notificationsCount
        );
    }

    private async Task ProcessStockArrivalNotificationAsync(StockMonitor monitor, StockChange change, CancellationToken cancellationToken)
    {
        try
        {
            // Idempotency check: prevent duplicate notifications within 2 minutes for the same variant
            var isDuplicate = await _repository.HasRecentNotificationAsync(monitor.Id, change.VariantName, TimeSpan.FromMinutes(2), cancellationToken);
            if (isDuplicate)
            {
                _logger.LogWarning("Duplicate notification suppressed for monitor {Id} ({Variant})", monitor.Id, change.VariantName);
                return;
            }

            // Usage Limit Enforcement: check if user has reached daily notification limit
            if (_usageLimitService != null && monitor.UserId > 0)
            {
                var canSend = await _usageLimitService.CanSendNotificationAsync(monitor.UserId, cancellationToken);
                if (!canSend)
                {
                    _logger.LogWarning("Daily notification limit reached for user {UserId}. Skipping Telegram alert for monitor {MonitorId}.", monitor.UserId, monitor.Id);
                    var limitHistory = new StockNotificationHistory
                    {
                        StockMonitorId = monitor.Id,
                        UserId = monitor.UserId,
                        VariantName = change.VariantName,
                        PreviousAvailability = false,
                        CurrentAvailability = true,
                        StockChangeAt = change.ChangedAt,
                        NotificationSentAt = DateTime.UtcNow,
                        Success = false,
                        Error = "Kullanıcı günlük bildirim limitine ulaştı."
                    };
                    await _repository.AddNotificationHistoryAsync(limitHistory, cancellationToken);
                    return;
                }
            }

            var notification = new StockAvailableNotification(
                MonitorId: monitor.Id,
                Store: monitor.Store,
                ProductName: monitor.ProductName,
                ProductUrl: monitor.ProductUrl,
                ImageUrl: monitor.ImageUrl,
                VariantName: change.VariantName,
                ProtectedTelegramBotToken: monitor.ProtectedTelegramBotToken,
                TelegramChatId: monitor.TelegramChatId
            );

            var sent = await _notificationService.NotifyStockAvailableAsync(notification, cancellationToken);

            var history = new StockNotificationHistory
            {
                StockMonitorId = monitor.Id,
                UserId = monitor.UserId > 0 ? monitor.UserId : null,
                VariantName = change.VariantName,
                PreviousAvailability = false,
                CurrentAvailability = true,
                StockChangeAt = change.ChangedAt,
                NotificationSentAt = DateTime.UtcNow,
                Success = sent,
                Error = sent ? null : "Telegram bildirimi gönderilemedi."
            };

            await _repository.AddNotificationHistoryAsync(history, cancellationToken);

            if (sent)
            {
                if (_usageLimitService != null && monitor.UserId > 0)
                {
                    await _usageLimitService.RecordNotificationUsageAsync(monitor.UserId, cancellationToken);
                }

                monitor.LastNotifiedAt = DateTime.UtcNow;
                monitor.LastNotifiedVariant = change.VariantName;
                _logger.LogInformation("Stock arrival notification recorded for monitor {Id} ({Variant})", monitor.Id, change.VariantName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process stock arrival notification for monitor ID: {Id}", monitor.Id);
        }
    }

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Bilinmeyen hata";
        if (message.Contains("403") || message.Contains("Cloudflare")) return "Mağaza erişim engeli (Bot koruması).";
        if (message.Contains("404")) return "Ürün sayfası bulunamadı.";
        return message.Length > 200 ? message[..200] + "..." : message;
    }
}
