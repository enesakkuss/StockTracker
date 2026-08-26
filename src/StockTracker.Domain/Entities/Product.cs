namespace StockTracker.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Brand { get; set; }
    public string StoreType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
