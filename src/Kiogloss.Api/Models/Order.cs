namespace Kiogloss.Api.Models;

/// <summary>store_order.</summary>
public class Order
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public virtual Account Account { get; set; } = null!;
    public double Amount { get; set; }
    public DateOnly Date { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PENDING;
    /// <summary>Dirección de entrega capturada en el checkout (snapshot, independiente del perfil).</summary>
    public string? ShippingAddress { get; set; }

    public virtual ICollection<DetailOrder> Shopping { get; set; } = new List<DetailOrder>();
}
