namespace Kiogloss.Api.Models;

/// <summary>Código de un solo uso (dura 30 minutos) para recuperar la contraseña. Tabla: store_password_reset_token.</summary>
public class PasswordResetToken
{
    public long Id { get; set; }
    public string Token { get; set; } = null!;
    public long UserId { get; set; }
    public virtual UserProfile User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}
