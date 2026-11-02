namespace Kiogloss.Api.Models;

/// <summary>Aviso guardado para un usuario o para los administradores. Tabla: store_notification.</summary>
public class Notification
{
    public long Id { get; set; }
    public long RecipientId { get; set; }
    public virtual UserProfile Recipient { get; set; } = null!;
    /// <summary>"USER" | "ADMIN"</summary>
    public string RecipientScope { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? RelatedEntityType { get; set; }
    public long? RelatedEntityId { get; set; }
    public bool Read { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
