using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

/// <summary>Imagen asociada a un producto. Tabla: store_images.</summary>
public class Images
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public virtual Product Product { get; set; } = null!;
    public string? Image { get; set; }

    /// <summary>Lombok @Data hashCode (id, image) — drives the HashSet order of Product#getImages.</summary>
    public int JavaHashCode() => JavaHash.Lombok(JavaHash.Of(Id), Image is null ? null : JavaHash.Of(Image));
}
