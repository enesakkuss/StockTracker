namespace StockTracker.Application.DTOs;

public record SubscriptionPlanDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string BillingPeriod,
    PlanLimitsDto Limits,
    bool IsActive
);

public record PlanLimitsDto(
    int MaxActiveMonitors,
    int MaxTotalMonitors,
    int MinCheckIntervalMinutes,
    bool TelegramEnabled,
    int MaxNotificationsPerDay,
    int MaxInspectRequestsPerDay
);

public record UserSubscriptionDto(
    int Id,
    int UserId,
    string Plan,
    string Status,
    DateTime StartedAt,
    DateTime? ExpiresAt,
    PlanLimitsDto Limits
);

public record UsageMetricsDto(
    int ActiveMonitors,
    int TotalMonitors,
    int NotificationsToday,
    int InspectRequestsToday
);

public record UsageSummaryDto(
    string Plan,
    string Status,
    PlanLimitsDto Limits,
    UsageMetricsDto Usage
);

// Payment abstraction DTOs (prepared for future payment gateways)
public record CheckoutRequestDto(
    int PlanId,
    string? SuccessUrl,
    string? CancelUrl
);

public record CheckoutResponseDto(
    string CheckoutUrl,
    string PaymentSessionId,
    string Status
);

public record PaymentStatusDto(
    string PaymentId,
    string Status,
    decimal Amount,
    string Currency
);
