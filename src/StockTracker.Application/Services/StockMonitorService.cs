using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTracker.Application.Common;
using StockTracker.Application.DTOs;
using StockTracker.Application.Interfaces;
using StockTracker.Domain.Entities;

namespace StockTracker.Application.Services;

public class StockMonitorService : IStockMonitorService
{
    private readonly IStockMonitorRepository _repository;
    private readonly ISecretProtector _secretProtector;
    private readonly IStoreAdapterResolver _adapterResolver;
    private readonly ILogger<StockMonitorService> _logger;
    private readonly int _minimumIntervalMinutes;

    public StockMonitorService(
        IStockMonitorRepository repository,
        ISecretProtector secretProtector,
        IStoreAdapterResolver adapterResolver,
        IConfiguration configuration,
        ILogger<StockMonitorService> logger)
    {
        _repository = repository;
        _secretProtector = secretProtector;
        _adapterResolver = adapterResolver;
        _logger = logger;

        if (!int.TryParse(configuration["Monitoring:MinimumIntervalMinutes"], out _minimumIntervalMinutes) || _minimumIntervalMinutes < 1)
        {
            _minimumIntervalMinutes = 1;
        }
    }

    public async Task<StockMonitorDto> CreateMonitorAsync(int userId, CreateMonitorRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreateRequest(request);

        var cleanedVariants = request.SelectedVariants
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanedVariants.Count == 0)
        {
            throw new ArgumentException("En az bir beden/varyant seçilmelidir.", nameof(request.SelectedVariants));
        }

        // Derive authentic store name directly from URL via Adapter Registry (prevents spoofing)
        var adapter = _adapterResolver.Resolve(request.ProductUrl);
        if (adapter is null)
        {
            throw new NotSupportedException($"Bu URL için desteklenen bir mağaza bulunamadı: {request.ProductUrl}");
        }
        var authenticStore = adapter.StoreName;

        var protectedToken = _secretProtector.Protect(request.TelegramBotToken.Trim());
        var now = DateTime.UtcNow;

        var monitor = new StockMonitor
        {
            UserId = userId,
            ProductUrl = request.ProductUrl.Trim(),
            Store = authenticStore,
            ProductName = request.ProductName.Trim(),
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
            SelectedVariants = cleanedVariants,
            ProtectedTelegramBotToken = protectedToken,
            TelegramChatId = request.TelegramChatId.Trim(),
            CheckIntervalMinutes = request.CheckIntervalMinutes > 0 ? request.CheckIntervalMinutes : 10,
            IsActive = true,
            CreatedAt = now,
            NextCheckAt = now // Ready for immediate first check by worker
        };

        var saved = await _repository.AddAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor created with ID: {Id} for user: {UserId}, store: {Store}, product: {ProductName}",
            saved.Id, userId, saved.Store, saved.ProductName);

        return MapToDto(saved);
    }

    public Task<StockMonitorDto> CreateMonitorAsync(CreateMonitorRequest request, CancellationToken cancellationToken = default)
    {
        return CreateMonitorAsync(1, request, cancellationToken);
    }

    public async Task<IReadOnlyList<StockMonitorDto>> GetMonitorsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var monitors = await _repository.GetAllByUserIdAsync(userId, cancellationToken);
        return monitors.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<StockMonitorDto>> GetMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var monitors = await _repository.GetAllAsync(cancellationToken);
        return monitors.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<StockMonitorDto>> GetPagedMonitorsAsync(int userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var paged = await _repository.GetPagedByUserIdAsync(userId, page, pageSize, cancellationToken);
        var dtos = paged.Items.Select(MapToDto).ToList();
        return new PagedResponse<StockMonitorDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize);
    }

    public async Task<StockMonitorDto?> GetMonitorByIdAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, userId, cancellationToken);
        return monitor is null ? null : MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> GetMonitorByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, cancellationToken);
        return monitor is null ? null : MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> UpdateMonitorAsync(int id, int userId, UpdateMonitorRequest request, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, userId, cancellationToken);
        if (monitor is null) return null;

        if (request.SelectedVariants != null && request.SelectedVariants.Count > 0)
        {
            var cleaned = request.SelectedVariants
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (cleaned.Count > 0)
            {
                monitor.SelectedVariants = cleaned;
            }
        }

        if (request.CheckIntervalMinutes.HasValue && request.CheckIntervalMinutes.Value >= _minimumIntervalMinutes)
        {
            monitor.CheckIntervalMinutes = request.CheckIntervalMinutes.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.TelegramChatId))
        {
            monitor.TelegramChatId = request.TelegramChatId.Trim();
        }

        monitor.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor updated (ID: {Id}, User: {UserId})", id, userId);

        return MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> StartMonitorAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, userId, cancellationToken);
        if (monitor is null) return null;

        monitor.IsActive = true;
        monitor.NextCheckAt = DateTime.UtcNow; // Check immediately upon starting
        monitor.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor started (ID: {Id}, User: {UserId})", id, userId);

        return MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> StartMonitorAsync(int id, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, cancellationToken);
        if (monitor is null) return null;

        monitor.IsActive = true;
        monitor.NextCheckAt = DateTime.UtcNow;
        monitor.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor started (ID: {Id})", id);

        return MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> StopMonitorAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, userId, cancellationToken);
        if (monitor is null) return null;

        monitor.IsActive = false;
        monitor.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor stopped (ID: {Id}, User: {UserId})", id, userId);

        return MapToDto(monitor);
    }

    public async Task<StockMonitorDto?> StopMonitorAsync(int id, CancellationToken cancellationToken = default)
    {
        var monitor = await _repository.GetByIdAsync(id, cancellationToken);
        if (monitor is null) return null;

        monitor.IsActive = false;
        monitor.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(monitor, cancellationToken);
        _logger.LogInformation("Stock monitor stopped (ID: {Id})", id);

        return MapToDto(monitor);
    }

    public async Task<bool> DeleteMonitorAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, userId, cancellationToken);
        if (deleted)
        {
            _logger.LogInformation("Stock monitor deleted (ID: {Id}, User: {UserId})", id, userId);
        }
        return deleted;
    }

    public async Task<bool> DeleteMonitorAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            _logger.LogInformation("Stock monitor deleted (ID: {Id})", id);
        }
        return deleted;
    }

    public async Task<PagedResponse<NotificationHistoryDto>> GetNotificationHistoriesAsync(int userId, NotificationQueryParams query, CancellationToken cancellationToken = default)
    {
        var paged = await _repository.GetPagedNotificationHistoriesByUserIdAsync(userId, query, cancellationToken);
        var dtos = paged.Items.Select(h => new NotificationHistoryDto(
            h.Id,
            h.StockMonitorId,
            h.StockMonitor?.Store ?? "Mağaza",
            h.StockMonitor?.ProductName ?? "Ürün",
            h.StockMonitor?.ImageUrl,
            h.VariantName,
            h.PreviousAvailability,
            h.CurrentAvailability,
            h.NotificationSentAt,
            h.Success,
            h.Error
        )).ToList();

        return new PagedResponse<NotificationHistoryDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize);
    }

    private void ValidateCreateRequest(CreateMonitorRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request), "İstek gövdesi boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductUrl))
        {
            throw new ArgumentException("Ürün URL'si boş olamaz.", nameof(request.ProductUrl));
        }

        if (!Uri.TryCreate(request.ProductUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Geçersiz ürün URL formatı.", nameof(request.ProductUrl));
        }

        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            throw new ArgumentException("Ürün adı boş olamaz.", nameof(request.ProductName));
        }

        if (request.SelectedVariants is null || request.SelectedVariants.Count == 0)
        {
            throw new ArgumentException("En az bir beden/varyant seçilmelidir.", nameof(request.SelectedVariants));
        }

        if (string.IsNullOrWhiteSpace(request.TelegramBotToken))
        {
            throw new ArgumentException("Telegram Bot Token boş olamaz.", nameof(request.TelegramBotToken));
        }

        if (string.IsNullOrWhiteSpace(request.TelegramChatId))
        {
            throw new ArgumentException("Telegram Chat ID boş olamaz.", nameof(request.TelegramChatId));
        }

        if (request.CheckIntervalMinutes < _minimumIntervalMinutes)
        {
            throw new ArgumentException($"Kontrol sıklığı en az {_minimumIntervalMinutes} dakika olmalıdır.", nameof(request.CheckIntervalMinutes));
        }
    }

    private static StockMonitorDto MapToDto(StockMonitor entity)
    {
        return new StockMonitorDto(
            entity.Id,
            entity.ProductUrl,
            entity.Store,
            entity.ProductName,
            entity.ImageUrl,
            entity.SelectedVariants.ToList(),
            entity.CheckIntervalMinutes,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.LastCheckedAt,
            entity.NextCheckAt,
            entity.LastCheckStatus,
            entity.LastCheckError,
            entity.LastNotifiedAt,
            entity.LastNotifiedVariant
        );
    }
}
