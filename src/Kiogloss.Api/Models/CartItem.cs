namespace Kiogloss.Api.Models;

/// <summary>store_cart_item.</summary>
public class CartItem
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public virtual Account Account { get; set; } = null!;
    public long VariantId { get; set; }
    public virtual ProductVariant Variant { get; set; } = null!;
    public int Quantity { get; set; } = 1;
}
