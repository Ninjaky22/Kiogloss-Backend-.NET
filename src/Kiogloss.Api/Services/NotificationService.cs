using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Configuration;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiogloss.Api.Services;

/// <summary>
/// Replacement for the ConcurrentHashMap&lt;Long, SseEmitter&gt; of NotificationService: one live
/// stream per user (a new subscription replaces the previous one, user and admin streams share
/// the map), frames written exactly like Spring's SseEmitter.
/// </summary>
public sealed class SseEmitterRegistry(ILogger<SseEmitterRegistry> logger)
{
    /// <summary>new SseEmitter(30L * 60 * 1000)</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<long, SseEmitter> _emitters = new();

    public sealed class SseEmitter(HttpResponse response)
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        public HttpResponse Response { get; } = response;

        public async Task Send(string frame)
        {
            await _lock.WaitAsync();
            try
            {
                await Response.WriteAsync(frame, Encoding.UTF8);
                await Response.Body.FlushAsync();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>NotificationService#subscribe — keeps the response open until timeout or client disconnect.</summary>
    public async Task Subscribe(long userId, HttpContext context)
    {
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream";

        var emitter = new SseEmitter(context.Response);
        _emitters[userId] = emitter;
        try
        {
            await emitter.Send("event:connected\ndata:ok\n\n");
            await Task.Delay(Timeout, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // client went away
        }
        catch (Exception e)
        {
            logger.LogDebug("SSE stream for user {UserId} ended: {Error}", userId, e.Message);
        }
        finally
        {
            _emitters.TryRemove(new KeyValuePair<long, SseEmitter>(userId, emitter));
        }
    }

    /// <summary>@Scheduled(fixedRate = 25000) sendHeartbeat</summary>
    public async Task SendHeartbeat()
    {
        var stale = new List<long>();
        foreach (var (userId, emitter) in _emitters)
        {
            try
            {
                await emitter.Send(":heartbeat\n\n");
            }
            catch (Exception)
            {
                stale.Add(userId);
            }
        }
        foreach (var userId in stale) _emitters.TryRemove(userId, out _);
    }

    public async Task PushToClient(long userId, NotificationDTO notification)
    {
        if (!_emitters.TryGetValue(userId, out var emitter)) return;
        try
        {
            await emitter.Send("event:notification\ndata:" + JsonSerializer.Serialize(notification, JsonConfig.Options) + "\n\n");
        }
        catch (Exception)
        {
            _emitters.TryRemove(new KeyValuePair<long, SseEmitter>(userId, emitter));
        }
    }
}

/// <summary>Port of com.todoteg.service.NotificationService.</summary>
public class NotificationService(
    KioglossDbContext db,
    IServiceScopeFactory scopeFactory,
    SseEmitterRegistry emitters,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<AppOptions> appOptions)
{
    private readonly int _lowStockThreshold = notificationOptions.Value.LowStockThreshold;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(appOptions.Value.Culture);

    // ==================== SSE ====================

    public Task Subscribe(long userId, HttpContext context) => emitters.Subscribe(userId, context);

    // ==================== BUSINESS EVENTS ====================
    // @Transactional(propagation = REQUIRES_NEW): every event runs in its own scope/DbContext and commits
    // on its own, even if the caller's transaction is rolled back afterwards (same as the Spring app).

    public async Task NotifyOrderCreated(long orderId, string customerName, double amount)
    {
        var msg = "Orden #" + orderId + " de " + customerName + " por COP " + amount.ToString("N0", _culture);
        await RequiresNew(async (ctx, tx) =>
        {
            var admins = await ctx.Users.Where(u => u.IsSuperuser == true).ToListAsync();
            foreach (var admin in admins)
            {
                var n = BuildNotification(admin.Id, "ADMIN", NotificationType.ORDER_CREATED,
                    "Nueva orden recibida", msg, "ORDER", orderId);
                await Save(ctx, n);
                await emitters.PushToClient(admin.Id, ToDTO(n));
            }
        });
    }

    public async Task NotifyOrderStatusChanged(long orderId, OrderStatus status, long userId)
    {
        var msg = "Tu orden #" + orderId + " ahora está: " + status.Description();
        await RequiresNew(async (ctx, tx) =>
        {
            var n = BuildNotification(userId, "USER", NotificationType.ORDER_STATUS_CHANGED,
                "Estado de tu pedido actualizado", msg, "ORDER", orderId);
            await Save(ctx, n);
            await emitters.PushToClient(userId, ToDTO(n));
        });
    }

    public async Task CheckAndNotifyStock(long variantId, int stock, string productName)
    {
        await RequiresNew(async (ctx, tx) =>
        {
            var oneHourAgo = DateTime.Now.AddHours(-1);
            if (stock == 0)
            {
                var recentExists = await ctx.Notifications.AnyAsync(n =>
                    n.Type == NotificationType.OUT_OF_STOCK && n.RelatedEntityId == variantId && n.CreatedAt > oneHourAgo);
                if (!recentExists)
                {
                    var msg = productName + " (var. #" + variantId + ") se quedó sin stock";
                    await FanOutToAdmins(ctx, NotificationType.OUT_OF_STOCK, "Producto sin stock", msg, "PRODUCT_VARIANT", variantId);
                }
            }
            else if (stock <= _lowStockThreshold)
            {
                var recentExists = await ctx.Notifications.AnyAsync(n =>
                    n.Type == NotificationType.LOW_STOCK && n.RelatedEntityId == variantId && n.CreatedAt > oneHourAgo);
                if (!recentExists)
                {
                    var msg = productName + " tiene solo " + stock + " unidades disponibles";
                    await FanOutToAdmins(ctx, NotificationType.LOW_STOCK, "Stock bajo", msg, "PRODUCT_VARIANT", variantId);
                }
            }
        });
    }

    private async Task FanOutToAdmins(KioglossDbContext ctx, NotificationType type, string title, string message,
        string entityType, long entityId)
    {
        var admins = await ctx.Users.Where(u => u.IsSuperuser == true).ToListAsync();
        foreach (var admin in admins)
        {
            var n = BuildNotification(admin.Id, "ADMIN", type, title, message, entityType, entityId);
            await Save(ctx, n);
            await emitters.PushToClient(admin.Id, ToDTO(n));
        }
    }

    // ==================== REST METHODS ====================

    public async Task<SpringPage<NotificationDTO>> GetNotifications(long userId, PageRequest pageable)
    {
        var page = await db.Notifications
            .Where(n => n.RecipientId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToSpringPageAsync(pageable);
        return page.Map(ToDTO);
    }

    public Task<long> GetUnreadCount(long userId) =>
        db.Notifications.LongCountAsync(n => n.RecipientId == userId && !n.Read);

    public async Task MarkRead(long notifId, long userId)
    {
        var n = await db.Notifications.FindAsync(notifId);
        if (n != null && n.RecipientId == userId && !n.Read)
        {
            n.Read = true;
            n.ReadAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
    }

    public async Task MarkAllRead(long userId)
    {
        var now = DateTime.Now;
        await db.Notifications
            .Where(n => n.RecipientId == userId && !n.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true).SetProperty(n => n.ReadAt, now));
    }

    public async Task Delete(long notifId, long userId)
    {
        var n = await db.Notifications.FindAsync(notifId);
        if (n != null && n.RecipientId == userId)
        {
            db.Notifications.Remove(n);
            await db.SaveChangesAsync();
        }
    }

    // ==================== HELPERS ====================

    private async Task RequiresNew(Func<KioglossDbContext, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction, Task> work)
    {
        using var scope = scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<KioglossDbContext>();
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await work(ctx, tx);
        await tx.CommitAsync();
    }

    private static async Task Save(KioglossDbContext ctx, Notification n)
    {
        n.CreatedAt = DateTime.Now; // @PrePersist
        ctx.Notifications.Add(n);
        await ctx.SaveChangesAsync();
    }

    private static Notification BuildNotification(long recipientId, string scope, NotificationType type, string title,
        string message, string entityType, long entityId) => new()
    {
        RecipientId = recipientId,
        RecipientScope = scope,
        Type = type,
        Title = title,
        Message = message,
        RelatedEntityType = entityType,
        RelatedEntityId = entityId,
    };

    public static NotificationDTO ToDTO(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Message = n.Message,
        RelatedEntityType = n.RelatedEntityType,
        RelatedEntityId = n.RelatedEntityId,
        Read = n.Read,
        CreatedAt = n.CreatedAt,
    };
}
