namespace Kiogloss.Api.Models;

/// <summary>Un producto que el cliente marcó como favorito. Tabla: store_detail_favoritos.</summary>
public class DetailFavoritos
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public virtual Account Account { get; set; } = null!;
    public long ProductId { get; set; }
    public virtual Product Product { get; set; } = null!;
    public DateOnly Date { get; set; }
    public bool Status { get; set; } = true;
}
