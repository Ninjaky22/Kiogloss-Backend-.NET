namespace Kiogloss.Api.Models;

/// <summary>Video publicado en la tienda (reel). Tabla: store_video_reel.</summary>
public class VideoReel
{
    public long Id { get; set; }
    public string? VideoUrl { get; set; }
    public string ThumbnailUrl { get; set; } = null!;
    public string Username { get; set; } = null!;
    public long ProductId { get; set; }
    public virtual Product Product { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
