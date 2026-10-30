using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

public enum OrderStatus
{
    PENDING,
    PROCESSING,
    SHIPPED,
    DELIVERED,
    CANCELLED,
}

public static class OrderStatusExtensions
{
    /// <summary>OrderStatus#getDescription (@JsonValue and database representation).</summary>
    public static string Description(this OrderStatus status) => status switch
    {
        OrderStatus.PENDING => "Pendiente",
        OrderStatus.PROCESSING => "Procesando",
        OrderStatus.SHIPPED => "Enviado",
        OrderStatus.DELIVERED => "Entregado",
        OrderStatus.CANCELLED => "Cancelado",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>OrderStatus.valueOf(value) — exact constant name.</summary>
    public static OrderStatus ValueOf(string value) =>
        Enum.GetNames<OrderStatus>().Contains(value)
            ? Enum.Parse<OrderStatus>(value)
            : throw new IllegalArgumentException($"No enum constant com.todoteg.model.OrderStatus.{value}");

    /// <summary>OrderStatus.fromString (@JsonCreator): name or description, case-insensitive.</summary>
    public static OrderStatus? FromString(string? value)
    {
        if (value == null) return null;
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            if (status.ToString().Equals(value, StringComparison.OrdinalIgnoreCase) ||
                status.Description().Equals(value, StringComparison.OrdinalIgnoreCase))
                return status;
        }
        if (value.Equals("processing", StringComparison.OrdinalIgnoreCase)) return OrderStatus.PROCESSING;
        if (value.Equals("en preparacion", StringComparison.OrdinalIgnoreCase)) return OrderStatus.PROCESSING;
        if (value.Equals("pending", StringComparison.OrdinalIgnoreCase)) return OrderStatus.PENDING;
        throw new IllegalArgumentException("Unknown status: " + value);
    }

    /// <summary>OrderStatusConverter#convertToEntityAttribute: description or name, falling back to PENDING.</summary>
    public static OrderStatus FromDatabase(string dbData)
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            if (status.Description().Equals(dbData, StringComparison.OrdinalIgnoreCase) ||
                status.ToString().Equals(dbData, StringComparison.OrdinalIgnoreCase))
                return status;
        }
        // Filas guardadas con la descripción anterior de PROCESSING
        if (dbData.Equals("En Preparacion", StringComparison.OrdinalIgnoreCase)) return OrderStatus.PROCESSING;
        return OrderStatus.PENDING;
    }
}
