namespace Kiogloss.Api.Models;

/// <summary>store_product_variant.</summary>
public class ProductVariant
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public virtual Product Product { get; set; } = null!;
    public virtual ICollection<VariantOption> Options { get; set; } = new List<VariantOption>();
    public int Stock { get; set; }
    public string? Sku { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Options iteration (identity hash codes in Java), ordered by id.</summary>
    public List<VariantOption> OptionsInSetOrder() => Options.OrderBy(o => o.Id).ToList();

    /// <summary>The "variantDetails"/"size" label: option values joined with ", ".</summary>
    public string OptionsLabel() => string.Join(", ", OptionsInSetOrder().Select(o => o.Value));
}
