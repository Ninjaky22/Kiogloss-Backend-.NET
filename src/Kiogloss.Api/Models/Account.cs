namespace Kiogloss.Api.Models;

/// <summary>Cuenta de compras de un usuario: sus puntos, su dirección, sus favoritos y sus pedidos. Tabla: store_account.</summary>
public class Account
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public virtual UserProfile User { get; set; } = null!;
    public long? AddressId { get; set; }
    public virtual Address? Address { get; set; }
    public int? PointsPerPurchase { get; set; } = 0;
    public bool? IsActive { get; set; } = true;

    public virtual ICollection<DetailFavoritos> Favorites { get; set; } = new List<DetailFavoritos>();
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
