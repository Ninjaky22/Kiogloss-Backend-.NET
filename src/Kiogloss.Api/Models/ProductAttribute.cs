namespace Kiogloss.Api.Models;

/// <summary>Característica libre de un producto (nombre/valor). Tabla: store_product_attributes.</summary>
public class ProductAttribute
{
    public long ProductId { get; set; }
    public string Name { get; set; } = null!;
    public string? Value { get; set; }
}
