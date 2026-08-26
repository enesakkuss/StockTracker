namespace StockTracker.Application.DTOs;

/// <summary>
/// Clean product response model for the frontend / mobile API consumers.
/// </summary>
public record ProductInspectResponse(
    string Store,
    string Name,
    string? ImageUrl,
    string Url,
    IReadOnlyList<VariantAvailabilityDto> Variants,
    string InspectStatus = "success",
    string? UserMessage = null
);

public record VariantAvailabilityDto(
    string Name,
    bool Available
);
