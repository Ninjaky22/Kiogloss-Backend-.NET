using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

/// <summary>Producto de la tienda: título, precio, etiquetas, imágenes y variantes. El "slug" es su nombre para la dirección web. Tabla: store_product.</summary>
public class Product
{
    public long Id { get; set; }
    public string Title { get; set; } = null!;
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public string Slug { get; set; } = null!;
    public string? Status { get; set; } = "draft";
    public DateTime Published { get; set; }

    /// <summary>@ElementCollection Map&lt;String,String&gt; attributes (store_product_attributes).</summary>
    public virtual ICollection<ProductAttribute> AttributeRows { get; set; } = new List<ProductAttribute>();
    public virtual ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public virtual ICollection<Images> Images { get; set; } = new List<Images>();

    /// <summary>Product#getAttributes as loaded by Hibernate (HashMap sized for the loaded rows, rows in result-set order).</summary>
    public Dictionary<string, string?> AttributesMap() =>
        JavaHash.LoadedOrder(AttributeRows.ToList(), a => JavaHash.Of(a.Name)).ToDictionary(a => a.Name, a => a.Value);

    /// <summary>Product#getImages().iterator().next() — first element of the Hibernate HashSet.</summary>
    public Images? FirstImageOfSet() => JavaHash.LoadedSetOrder(Images.ToList(), i => i.JavaHashCode()).FirstOrDefault();

    /// <summary>Product#getTags() iteration as loaded by Hibernate (Lombok hashCode of Tag drives the HashSet order).</summary>
    public List<Tag> TagsInSetOrder() => JavaHash.LoadedSetOrder(Tags.ToList(), t => t.JavaHashCode());

    /// <summary>Product#getVariants() iteration. Variants use identity hash codes in Java, so id order is used.</summary>
    public List<ProductVariant> VariantsInSetOrder() => Variants.OrderBy(v => v.Id == 0 ? long.MaxValue : v.Id).ToList();
}
