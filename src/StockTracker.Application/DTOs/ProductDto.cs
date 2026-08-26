namespace StockTracker.Application.DTOs;

public record ProductDto(
    int Id,
    string Url,
    string Name,
    string? ImageUrl,
    string? Brand,
    string StoreType,
    DateTime CreatedAt,
    IReadOnlyList<ProductVariantDto> Variants
);
