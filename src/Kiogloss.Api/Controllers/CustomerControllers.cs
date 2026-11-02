// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Direcciones (rutas) del cliente con sesión iniciada: pedidos, carrito y notificaciones.
// ======================================

using System.Globalization;
using System.Text.Json;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Models;
using Kiogloss.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Controllers;

/// <summary>Pedidos del cliente: hacer un pedido (POST /user/order) y ver los que ya hizo (GET /user/orders).</summary>
[Route("user")]
[OrderControllerExceptionHandler]
public sealed class OrderController(OrderService orderService) : JavaControllerBase
{
    /// <summary>POST /user/order - Crear nueva orden</summary>
    [HttpPost("order")]
    public async Task<IActionResult> CreateOrder()
    {
        var request = await ReadBody<OrderCreateRequest>(
            Spring + "<java.lang.Void> com.todoteg.controller.OrderController.createOrder(com.todoteg.dto.OrderCreateRequest)",
            "orderCreateRequest");
        await orderService.CreateOrder(request);
        return EmptyStatus(201);
    }

    /// <summary>GET /user/orders - Listar órdenes del usuario autenticado, con filtros por fecha y estado</summary>
    [HttpGet("orders")]
    public async Task<IActionResult> GetUserOrders()
    {
        var statusOrder = OptionalParam("statusOrder");
        var after = DateParam("rangeDate_after");
        var before = DateParam("rangeDate_before");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 8);

        var user = CurrentUser;
        var pageable = PageRequest.Of(page, pageSize);

        OrderStatus? status = null;
        if (!string.IsNullOrEmpty(statusOrder) && !statusOrder.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            status = OrderStatusExtensions.FromString(statusOrder);

        return Json(await orderService.GetUserOrders(user.Id, status, after, before, pageable));
    }

    /// <summary>@RequestParam @DateTimeFormat(iso = ISO.DATE) LocalDate</summary>
    private DateOnly? DateParam(string name)
    {
        var raw = Request.Query[name].ToString();
        if (raw.Length == 0) return null;
        return DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw JavaErrors.DateTypeMismatch(name, raw);
    }

    /// <summary>@ExceptionHandler(MethodArgumentTypeMismatchException.class) local to this controller.</summary>
    private sealed class OrderControllerExceptionHandlerAttribute : ExceptionFilterAttribute
    {
        public override void OnException(ExceptionContext context)
        {
            if (context.Exception is JavaException { JavaType: JavaErrors.TypeMismatchType })
            {
                context.Result = Json(new ErrorResponse("Formato de fecha inválido. Use el formato: YYYY-MM-DD (ejemplo: 2024-01-15)"), 400);
                context.ExceptionHandled = true;
            }
        }
    }
}

/// <summary>Carrito del cliente (/user/cart): ver, agregar, cambiar la cantidad, quitar y vaciar.
/// Excepción: aquí las reglas están dentro del mismo controlador (así venía del sistema anterior).</summary>
[Route("user/cart")]
public sealed class CartController(KioglossDbContext db) : JavaControllerBase
{
    private static readonly string[] ItemKeys = ["id", "productId", "variantId", "name", "price", "slug", "quantity", "variantDetails", "stock", "image"];

    /// <summary>GET /user/cart - Devuelve el carrito del usuario autenticado.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetCart()
    {
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 8);

        var user = CurrentUser;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id) ?? throw JavaErrors.NoSuchElement();

        var cartPage = await db.CartItems.Where(c => c.AccountId == account.Id).OrderBy(c => c.Id)
            .ToSpringPageAsync(PageRequest.Of(page, pageSize));

        // same SQL Hibernate generated for sumGrandTotalByAccountId: numeric * integer keeps the column scale (2)
        var grandTotal = await db.Database.SqlQuery<decimal>($"""
            SELECT COALESCE(SUM(p.price * c.quantity), 0) AS "Value"
            FROM store_cart_item c
            JOIN store_product_variant v ON v.id = c.variant_id
            JOIN store_product p ON p.id = v.product_id
            WHERE c.account_id = {account.Id}
            """).SingleAsync();

        var items = cartPage.Content.Select(item =>
        {
            var variant = item.Variant;
            var product = variant.Product;
            // new HashMap<>(): the key order of the JSON object is the HashMap iteration order
            var values = new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["productId"] = product.Id,
                ["variantId"] = variant.Id,
                ["name"] = product.Title,
                ["price"] = product.Price,
                ["slug"] = product.Slug,
                ["quantity"] = item.Quantity,
                ["variantDetails"] = variant.OptionsLabel(),
                ["stock"] = variant.Stock,
            };
            if (variant.ImageUrl != null)
                values["image"] = variant.ImageUrl;
            else if (product.Images.Count > 0)
                values["image"] = product.FirstImageOfSet()!.Image;

            return JavaHash.DefaultMapOrder(ItemKeys.Where(values.ContainsKey)).ToDictionary(k => k, k => values[k]);
        }).ToList();

        var response = new Dictionary<string, object?>
        {
            ["grandTotal"] = grandTotal,
            ["content"] = items,
            ["totalElements"] = cartPage.TotalElements,
            ["totalPages"] = cartPage.TotalPages,
            ["number"] = cartPage.Number,
        };
        return Json(response);
    }

    /// <summary>POST /user/cart - Agrega una variante al carrito.</summary>
    [HttpPost("")]
    public async Task<IActionResult> AddToCart()
    {
        var req = await ReadBody<Dictionary<string, JsonElement>>(
            Spring + "<?> com.todoteg.controller.CartController.addToCart(org.springframework.security.core.Authentication,java.util.Map<java.lang.String, java.lang.Object>)");

        await using var tx = await db.Database.BeginTransactionAsync();
        var user = CurrentUser;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id) ?? throw JavaErrors.NoSuchElement();
        var variantId = ParseJavaLong(MapValueToString(req, "variantId"));
        var variant = await db.ProductVariants.FindAsync(variantId) ?? throw JavaErrors.NoSuchElement();

        var qty = ParseJavaInt(MapValueToString(req, "quantity"));

        var item = await db.CartItems.FirstOrDefaultAsync(c => c.AccountId == account.Id && c.VariantId == variantId);
        if (item == null)
        {
            db.CartItems.Add(new CartItem { AccountId = account.Id, VariantId = variant.Id, Quantity = qty });
        }
        else
        {
            item.Quantity += qty;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return EmptyStatus(200);
    }

    /// <summary>PUT /user/cart/{id} - Cambia la cantidad de un ítem del carrito.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateQuantity(string id)
    {
        var itemId = PathLong("id", id);
        var quantity = RequiredIntParam("quantity");
        var item = await db.CartItems.FindAsync(itemId) ?? throw JavaErrors.NoSuchElement();
        item.Quantity = quantity;
        await db.SaveChangesAsync();
        return EmptyStatus(200);
    }

    /// <summary>DELETE /user/cart/{id} - Quita un ítem del carrito.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveItem(string id)
    {
        // CrudRepository#deleteById ignores missing ids
        var item = await db.CartItems.FindAsync(PathLong("id", id));
        if (item != null)
        {
            db.CartItems.Remove(item);
            await db.SaveChangesAsync();
        }
        return EmptyStatus(200);
    }

    /// <summary>DELETE /user/cart - Vacía el carrito.</summary>
    [HttpDelete("")]
    public async Task<IActionResult> ClearCart()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = CurrentUser;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == user.Id) ?? throw JavaErrors.NoSuchElement();
        var items = await db.CartItems.Where(c => c.AccountId == account.Id).ToListAsync();
        db.CartItems.RemoveRange(items);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return EmptyStatus(200);
    }

    /// <summary>req.get(key).toString() on the Map Jackson built (Integer/Long/Double/String/Boolean values).</summary>
    private static string MapValueToString(Dictionary<string, JsonElement> req, string key)
    {
        if (!req.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null)
            throw JavaErrors.NullPointer("Cannot invoke \"Object.toString()\" because the return value of \"java.util.Map.get(Object)\" is null");
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : JavaFormat.Double(value.GetDouble()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetRawText(),
        };
    }

    private static long ParseJavaLong(string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw Compat.IllegalArgumentException.NumberFormat(text);

    private static int ParseJavaInt(string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw Compat.IllegalArgumentException.NumberFormat(text);
}

/// <summary>Notificaciones: ver la lista, contar las no leídas, marcar como leída, borrar y recibirlas en vivo (/stream).
/// Hay una versión para clientes (/user/) y otra para administradores (/admin/), que funcionan igual.</summary>
public sealed class NotificationController(NotificationService notificationService) : JavaControllerBase
{
    // ==================== USUARIO ====================

    /// <summary>GET /user/notifications/stream - Canal SSE de notificaciones del cliente.</summary>
    [HttpGet("user/notifications/stream")]
    public Task StreamUser() => Stream();

    /// <summary>GET /user/notifications - Lista paginada de notificaciones del cliente.</summary>
    [HttpGet("user/notifications")]
    public Task<IActionResult> ListUser() => List();

    /// <summary>GET /user/notifications/unread-count - Cantidad de notificaciones sin leer del cliente.</summary>
    [HttpGet("user/notifications/unread-count")]
    public Task<IActionResult> UnreadCountUser() => UnreadCount();

    /// <summary>PATCH /user/notifications/{id}/read - Marca una notificación del cliente como leída.</summary>
    [HttpPatch("user/notifications/{id}/read")]
    public Task<IActionResult> MarkReadUser(string id) => MarkRead(id);

    /// <summary>PATCH /user/notifications/read-all - Marca todas las notificaciones del cliente como leídas.</summary>
    [HttpPatch("user/notifications/read-all")]
    public Task<IActionResult> MarkAllReadUser() => MarkAllRead();

    /// <summary>DELETE /user/notifications/{id} - Elimina una notificación del cliente.</summary>
    [HttpDelete("user/notifications/{id}")]
    public Task<IActionResult> DeleteUser(string id) => Delete(id);

    // ==================== ADMIN ====================

    /// <summary>GET /admin/notifications/stream - Canal SSE de notificaciones del administrador.</summary>
    [HttpGet("admin/notifications/stream")]
    public Task StreamAdmin() => Stream();

    /// <summary>GET /admin/notifications - Lista paginada de notificaciones del administrador.</summary>
    [HttpGet("admin/notifications")]
    public Task<IActionResult> ListAdmin() => List();

    /// <summary>GET /admin/notifications/unread-count - Cantidad de notificaciones sin leer del administrador.</summary>
    [HttpGet("admin/notifications/unread-count")]
    public Task<IActionResult> UnreadCountAdmin() => UnreadCount();

    /// <summary>PATCH /admin/notifications/{id}/read - Marca una notificación del administrador como leída.</summary>
    [HttpPatch("admin/notifications/{id}/read")]
    public Task<IActionResult> MarkReadAdmin(string id) => MarkRead(id);

    /// <summary>PATCH /admin/notifications/read-all - Marca todas las notificaciones del administrador como leídas.</summary>
    [HttpPatch("admin/notifications/read-all")]
    public Task<IActionResult> MarkAllReadAdmin() => MarkAllRead();

    /// <summary>DELETE /admin/notifications/{id} - Elimina una notificación del administrador.</summary>
    [HttpDelete("admin/notifications/{id}")]
    public Task<IActionResult> DeleteAdmin(string id) => Delete(id);

    // ==================== shared ====================

    /// <summary>
    /// produces = text/event-stream. Spring's content negotiation falls back to application/json
    /// (WebConfig default) when Accept is missing or only */*, which makes the request fail with 406 -> 500.
    /// </summary>
    private async Task Stream()
    {
        if (!AcceptsEventStream(Request.Headers.Accept.ToString())) throw JavaErrors.NotAcceptable();
        await notificationService.Subscribe(CurrentUser.Id, HttpContext);
    }

    private async Task<IActionResult> List()
    {
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 20);
        var user = CurrentUser;
        return Json(await notificationService.GetNotifications(user.Id, PageRequest.Of(page, pageSize)));
    }

    private async Task<IActionResult> UnreadCount() =>
        Json(new Dictionary<string, long> { ["count"] = await notificationService.GetUnreadCount(CurrentUser.Id) });

    private async Task<IActionResult> MarkRead(string id)
    {
        var notifId = PathLong("id", id);
        await notificationService.MarkRead(notifId, CurrentUser.Id);
        return EmptyStatus(204);
    }

    private async Task<IActionResult> MarkAllRead()
    {
        await notificationService.MarkAllRead(CurrentUser.Id);
        return EmptyStatus(204);
    }

    private async Task<IActionResult> Delete(string id)
    {
        var notifId = PathLong("id", id);
        await notificationService.Delete(notifId, CurrentUser.Id);
        return EmptyStatus(204);
    }

    private static bool AcceptsEventStream(string accept)
    {
        if (string.IsNullOrWhiteSpace(accept)) return false;
        var types = accept.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(';')[0].Trim().ToLowerInvariant()).ToList();
        if (types.All(t => t == "*/*")) return false;
        return types.Any(t => t is "text/event-stream" or "text/*" or "*/*");
    }
}
