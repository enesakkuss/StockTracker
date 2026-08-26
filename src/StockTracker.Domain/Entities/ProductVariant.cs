namespace StockTracker.Domain.Entities;

public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Size { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public bool IsInStock { get; set; }
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
}
