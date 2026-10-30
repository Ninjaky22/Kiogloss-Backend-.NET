using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

/// <summary>store_tag.</summary>
public class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ImageURL { get; set; }

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    /// <summary>Lombok @Data hashCode (id, name, imageURL).</summary>
    public int JavaHashCode() =>
        JavaHash.Lombok(JavaHash.Of(Id), Name is null ? null : JavaHash.Of(Name), ImageURL is null ? null : JavaHash.Of(ImageURL));
}
