namespace Kiogloss.Api.Models;

/// <summary>Una línea de un pedido: qué producto, cuántas unidades y a qué precio. Tabla: store_detail_order.</summary>
public class DetailOrder
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public virtual Order Order { get; set; } = null!;
    public long VariantId { get; set; }
    public virtual ProductVariant Variant { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public double Price { get; set; }
}
