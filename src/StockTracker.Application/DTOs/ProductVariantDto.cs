namespace StockTracker.Application.DTOs;

public record ProductVariantDto(
    int Id,
    string Size,
    string? Sku,
    bool IsInStock
);
