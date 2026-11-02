namespace Kiogloss.Api.Models;

/// <summary>Razones por las que el sistema envía un aviso.</summary>
public enum NotificationType
{
    /// <summary>Un cliente hizo un pedido (se avisa a los administradores).</summary>
    ORDER_CREATED,
    /// <summary>Un administrador cambió la etapa de un pedido (se avisa al cliente).</summary>
    ORDER_STATUS_CHANGED,
    /// <summary>Quedan pocas unidades de un producto (el límite se define en Notifications:LowStockThreshold).</summary>
    LOW_STOCK,
    /// <summary>Se acabaron las unidades de un producto.</summary>
    OUT_OF_STOCK,
}
