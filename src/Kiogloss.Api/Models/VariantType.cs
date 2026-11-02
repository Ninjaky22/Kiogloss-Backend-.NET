namespace Kiogloss.Api.Models;

/// <summary>Característica en que varía un producto (ej. Color, Talla) con sus opciones. Tabla: store_variant_type.</summary>
public class VariantType
{
    public long Id { get; set; }
    /// <summary>e.g. "Color", "Talla", "Modelo"</summary>
    public string Name { get; set; } = null!;
    /// <summary>Display hint for the frontend ("color", "button", "select").</summary>
    public string? Type { get; set; } = "button";

    public virtual ICollection<VariantOption> Options { get; set; } = new List<VariantOption>();
}
