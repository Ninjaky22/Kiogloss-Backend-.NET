namespace Kiogloss.Api.Models;

/// <summary>Row of store_product_attributes (Product.attributes map entry).</summary>
public class ProductAttribute
{
    public long ProductId { get; set; }
    public string Name { get; set; } = null!;
    public string? Value { get; set; }
}
