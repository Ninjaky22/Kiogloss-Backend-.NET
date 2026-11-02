namespace Kiogloss.Api.Models;

/// <summary>Una opción posible (ej. "Rojo" dentro de "Color"). Tabla: store_variant_option.</summary>
public class VariantOption
{
    public long Id { get; set; }
    public long VariantTypeId { get; set; }
    public virtual VariantType VariantType { get; set; } = null!;
    /// <summary>e.g. "Rojo", "M", "GTX 1060"</summary>
    public string Value { get; set; } = null!;
    /// <summary>Only used if VariantType.type == "color" (e.g. "#FF0000")</summary>
    public string? MetaValue { get; set; }

    public virtual ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
