// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Tareas que se hacen solas, sin que nadie las pida.
// Una mantiene abiertas las notificaciones en vivo; la otra crea datos de ejemplo cuando la tienda está vacía.
// ======================================

using Kiogloss.Api.Configuration;
using Kiogloss.Api.Data;
using Kiogloss.Api.Models;
using Kiogloss.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiogloss.Api.Background;

/// <summary>Cada 25 segundos envía una señal pequeña ("latido") a las notificaciones en vivo para que la conexión no se cierre por estar quieta.</summary>
public sealed class SseHeartbeatService(SseEmitterRegistry emitters) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25000));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await emitters.SendHeartbeat();
    }
}

/// <summary>Al encender el sistema, si la tienda está vacía, crea datos de ejemplo. Si ya hay datos, no hace nada.</summary>
public sealed class DatabaseSeeder(IServiceScopeFactory scopeFactory, IOptions<AppOptions> options, ILogger<DatabaseSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.SeedDatabase) return;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KioglossDbContext>();
        if (await db.Products.LongCountAsync(cancellationToken) != 0) return;

        logger.LogInformation("Seeding database with professional dynamic e-commerce data...");

        // 1. Create Variant Types
        var vtColor = new VariantType { Name = "Color", Type = "color" };
        var vtSize = new VariantType { Name = "Talla", Type = "button" };
        var vtModel = new VariantType { Name = "Modelo", Type = "button" };
        db.VariantTypes.AddRange(vtColor, vtSize, vtModel);
        await db.SaveChangesAsync(cancellationToken);

        // 2. Create Options
        var optBlack = new VariantOption { VariantType = vtColor, Value = "Negro", MetaValue = "#000000" };
        var optWhite = new VariantOption { VariantType = vtColor, Value = "Blanco", MetaValue = "#ffffff" };
        var optSizeM = new VariantOption { VariantType = vtSize, Value = "M" };
        var optSizeL = new VariantOption { VariantType = vtSize, Value = "L" };
        var optGtx1060 = new VariantOption { VariantType = vtModel, Value = "GTX 1060" };
        var optGtx1060S = new VariantOption { VariantType = vtModel, Value = "GTX 1060 Super" };
        foreach (var option in new[] { optBlack, optWhite, optSizeM, optSizeL, optGtx1060, optGtx1060S })
        {
            db.VariantOptions.Add(option);
            await db.SaveChangesAsync(cancellationToken);
        }

        // 3. Create Tags
        var tagTech = new Tag { Name = "Tecnología" };
        db.Tags.Add(tagTech);
        await db.SaveChangesAsync(cancellationToken);

        // 4. Create Product: GPU (+ variants)
        var p1 = new Product
        {
            Title = "Tarjeta Gráfica Gamer",
            Slug = "tarjeta-grafica-gamer",
            Price = 850000.00m,
            Description = "<h2>Potencia gráfica</h2><p>Ideal para gaming en 1080p.</p>",
            Status = "published",
            Published = DateTime.Now,
        };
        p1.Tags.Add(tagTech);
        var v1 = new ProductVariant { Product = p1, Stock = 5, Sku = "GPU-1060-STD" };
        v1.Options.Add(optGtx1060);
        v1.Options.Add(optBlack);
        var v2 = new ProductVariant { Product = p1, Stock = 2, Sku = "GPU-1060-SUP" };
        v2.Options.Add(optGtx1060S);
        v2.Options.Add(optWhite);
        p1.Variants.Add(v1);
        p1.Variants.Add(v2);
        db.Products.Add(p1);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Database seeding completed!");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
