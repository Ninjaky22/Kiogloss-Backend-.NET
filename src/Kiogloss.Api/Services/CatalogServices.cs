// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Reglas del catálogo: productos y etiquetas, favoritos, videos y los reportes del administrador.
// ======================================

using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Services;

/// <summary>Catálogo de la tienda: lista de productos (con filtros, orden y páginas), detalle de un producto y etiquetas.</summary>
public sealed class ProductService(KioglossDbContext db, CloudinaryService cloudinaryService)
{
    /// <summary>Criterio de ordenamiento del catálogo público.</summary>
    public enum SortMode { Latest, PriceLow, PriceHigh }

    /// <summary>Lista paginada de productos publicados, filtrable por texto y etiquetas, con el orden indicado.</summary>
    public async Task<SpringPage<ProductListDTO>> GetPublishedProducts(string? search, List<string>? tags,
        SortMode sort, PageRequest pageable)
    {
        SpringPage<Product> products;

        if (tags != null && tags.Count > 0)
        {
            var lowerTags = tags.Select(t => t.ToLower()).ToList();
            var query = db.Products.Where(p => p.Status == "published" && p.Tags.Any(t => lowerTags.Contains(t.Name.ToLower())));
            var total = await query.LongCountAsync();
            // the explicit JPQL always orders by published DESC, whatever "sort" says
            var content = await query.OrderByDescending(p => p.Published)
                .Skip((int)pageable.Offset).Take(pageable.PageSize).ToListAsync();
            products = new SpringPage<Product>(content, pageable, total);
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            products = await SearchByWords(search.Trim(), pageable);
        }
        else
        {
            var query = db.Products.Where(p => p.Status == "published");
            query = sort switch
            {
                SortMode.PriceLow => query.OrderBy(p => p.Price),
                SortMode.PriceHigh => query.OrderByDescending(p => p.Price),
                _ => query.OrderByDescending(p => p.Published),
            };
            products = await query.ToSpringPageAsync(pageable);
        }

        var dtos = new List<ProductListDTO>();
        foreach (var p in products.Content) dtos.Add(await ConvertToListDTO(p));
        return new SpringPage<ProductListDTO>(dtos, pageable, products.TotalElements);
    }

    /// <summary>Obtiene un producto publicado por su slug.</summary>
    public async Task<ProductDetailDTO> GetProductBySlug(string slug)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Slug == slug)
                      ?? throw new Compat.RuntimeException("Product not found");
        return await ConvertToDetailDTO(product);
    }

    /// <summary>Lista todas las etiquetas disponibles en el catálogo.</summary>
    public async Task<List<TagDTO>> GetAllTags() =>
        (await db.Tags.ToListAsync()).Select(t => new TagDTO(t.Id, t.Name, t.ImageURL)).ToList();

    /// <summary>
    /// Búsqueda por palabras individuales (mínimo 3 caracteres): una cláusula OR por palabra sobre
    /// título, nombre de tag y valor de opción de variante.
    /// </summary>
    private async Task<SpringPage<Product>> SearchByWords(string search, PageRequest pageable)
    {
        var words = Regex.Split(search, "\\s+").Where(part => part.Length >= 3).Select(part => part.ToLower()).ToList();
        if (words.Count == 0) words.Add(search.ToLower());

        Expression<Func<Product, bool>>? condition = null;
        foreach (var word in words)
        {
            var pattern = "%" + word + "%";
            Expression<Func<Product, bool>> wordCondition = p =>
                EF.Functions.Like(p.Title.ToLower(), pattern)
                || p.Tags.Any(t => EF.Functions.Like(t.Name.ToLower(), pattern))
                || p.Variants.Any(v => v.Options.Any(o => EF.Functions.Like(o.Value.ToLower(), pattern)));
            condition = condition == null ? wordCondition : condition.OrElse(wordCondition);
        }

        var query = db.Products.Where(p => p.Status == "published").Where(condition!);
        var total = await query.LongCountAsync();
        var content = await query.OrderByDescending(p => p.Published)
            .Skip((int)pageable.Offset).Take(pageable.PageSize).ToListAsync();
        return new SpringPage<Product>(content, pageable, total);
    }

    private async Task<ProductListDTO> ConvertToListDTO(Product product)
    {
        var images = await db.Images.Where(i => i.ProductId == product.Id).ToListAsync();
        var variants = product.VariantsInSetOrder();
        var hasRealVariants = variants.Any(v => v.Options.Count > 0);
        return new ProductListDTO
        {
            Id = product.Id,
            Image = images.Count == 0 ? "" : cloudinaryService.GetImageUrl(images[0].Image),
            Title = product.Title,
            Slug = product.Slug,
            Price = JavaFormat.BigDecimal(product.Price),
            Stock = variants.Sum(v => v.Stock),
            HasVariants = hasRealVariants,
            DefaultVariantId = !hasRealVariants && variants.Count > 0 ? variants[0].Id : null,
        };
    }

    private async Task<ProductDetailDTO> ConvertToDetailDTO(Product product)
    {
        var images = await db.Images.Where(i => i.ProductId == product.Id).ToListAsync();
        var variants = product.Variants.OrderBy(v => v.Id).Select(v => new ProductVariantDTO(
            v.Id,
            v.Options.OrderBy(o => o.Id)
                .Select(o => new VariantOptionDTO(o.Id, o.VariantType.Name, o.Value, o.MetaValue)).ToList(),
            v.Stock, v.Sku, v.ImageUrl)).ToList();

        return new ProductDetailDTO
        {
            Id = product.Id,
            Images = images.Select(img => cloudinaryService.GetImageUrl(img.Image)).ToList(),
            Title = product.Title,
            Description = product.Description,
            Tags = product.TagsInSetOrder().Select(t => t.Name).ToList(),
            Price = JavaFormat.BigDecimal(product.Price),
            Stock = product.Variants.Sum(v => v.Stock),
            Attributes = product.AttributesMap(),
            Variants = variants,
        };
    }
}

/// <summary>Favoritos: agregar, quitar y ver los productos favoritos de un cliente.</summary>
public sealed class FavoriteService(KioglossDbContext db, CloudinaryService cloudinaryService)
{
    /// <summary>Agrega un producto a los favoritos de una cuenta.</summary>
    public async Task<long> AddFavorite(FavoriteCreateRequest request)
    {
        var account = await db.Accounts.FindAsync(request.Account!.Value) ?? throw new Compat.RuntimeException("Account not found");
        var product = await db.Products.FindAsync(request.Product!.Value) ?? throw new Compat.RuntimeException("Product not found");

        var favorite = new DetailFavoritos
        {
            AccountId = account.Id,
            ProductId = product.Id,
            Date = DateOnly.FromDateTime(DateTime.Now),
            Status = true,
        };
        db.Favorites.Add(favorite);
        await db.SaveChangesAsync();
        return favorite.Id;
    }

    /// <summary>Quita un favorito por id.</summary>
    public async Task RemoveFavorite(long favoriteId)
    {
        var favorite = await db.Favorites.FindAsync(favoriteId) ?? throw new Compat.RuntimeException("Favorite not found");
        db.Favorites.Remove(favorite);
        await db.SaveChangesAsync();
    }

    /// <summary>Obtiene los favoritos de una cuenta.</summary>
    public async Task<AccountFavoritesResponse> GetAccountFavorites(long accountId)
    {
        var account = await db.Accounts.FindAsync(accountId) ?? throw new Compat.RuntimeException("Account not found");
        var favoritos = await db.Favorites.Where(f => f.AccountId == accountId).ToListAsync();

        var favoriteIds = favoritos.Select(fav => new FavoriteIdDTO(fav.Id, fav.ProductId)).ToList();

        object favoriteData;
        if (favoritos.Count == 0)
        {
            favoriteData = "No tienes favoritos";
        }
        else
        {
            var items = new List<FavoriteProductItemDTO>();
            foreach (var fav in favoritos)
            {
                var product = fav.Product;
                var images = await db.Images.Where(i => i.ProductId == product.Id).ToListAsync();
                items.Add(new FavoriteProductItemDTO
                {
                    Id = product.Id,
                    Name = product.Title,
                    Price = JavaFormat.BigDecimal(product.Price),
                    Slug = product.Slug,
                    Image = images.Count == 0 ? "" : cloudinaryService.GetImageUrl(images[0].Image),
                    DefaultVariantId = product.VariantsInSetOrder().FirstOrDefault()?.Id,
                });
            }
            favoriteData = items;
        }

        return new AccountFavoritesResponse(new AccountFavoritesDTO
        {
            Id = account.Id,
            FavoriteID = favoriteIds,
            Favorite = favoriteData,
            Address = account.Address != null ? account.Address.ToString() : "",
            City = account.Address != null ? account.Address.City : "",
        });
    }
}

/// <summary>Videos: el público ve solo los publicados; el administrador maneja todos.</summary>
public sealed class VideoService(KioglossDbContext db, FileStorageService fileStorageService)
{
    /// <summary>Lista todos los videos (uso administrativo).</summary>
    public async Task<List<VideoReelDTO>> GetAllVideos() =>
        (await db.VideoReels.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).ToListAsync()).Select(ToDTO).ToList();

    /// <summary>
    /// Lista los videos que ve el público: todos los que el administrador cargó.
    /// Antes se ocultaban los videos cuyo producto estaba en "Borrador", y por eso videos cargados
    /// en el panel no aparecían en la tienda sin ningún aviso.
    /// </summary>
    public async Task<List<VideoReelDTO>> GetPublishedVideos() =>
        (await db.VideoReels.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).ToListAsync())
        .Select(ToDTO).ToList();

    /// <summary>Crea un video.</summary>
    public async Task<VideoReelDTO> CreateVideo(VideoCreateRequest request)
    {
        var product = await db.Products.FindAsync(request.ProductId!.Value)
                      ?? throw new ResourceNotFoundException("Producto no encontrado");
        var video = new VideoReel
        {
            VideoUrl = request.VideoUrl,
            ThumbnailUrl = request.ThumbnailUrl!,
            Username = request.Username!,
            Product = product,
            CreatedAt = DateTime.Now,
        };
        db.VideoReels.Add(video);
        await db.SaveChangesAsync();
        return ToDTO(video);
    }

    /// <summary>Actualiza un video.</summary>
    public async Task<VideoReelDTO> UpdateVideo(long id, VideoUpdateRequest request)
    {
        var video = await FindById(id);
        if (request.VideoUrl != null) video.VideoUrl = request.VideoUrl;
        if (request.ThumbnailUrl != null) video.ThumbnailUrl = request.ThumbnailUrl;
        if (request.Username != null) video.Username = request.Username;
        if (request.ProductId != null)
        {
            video.Product = await db.Products.FindAsync(request.ProductId.Value)
                            ?? throw new ResourceNotFoundException("Producto no encontrado");
        }
        await db.SaveChangesAsync();
        return ToDTO(video);
    }

    /// <summary>Elimina un video.</summary>
    public async Task DeleteVideo(long id)
    {
        var video = await FindById(id);
        fileStorageService.DeleteIfLocal(video.ThumbnailUrl);
        fileStorageService.DeleteIfLocal(video.VideoUrl);
        db.VideoReels.Remove(video);
        await db.SaveChangesAsync();
    }

    private async Task<VideoReel> FindById(long id) =>
        await db.VideoReels.FindAsync(id) ?? throw new ResourceNotFoundException("Video no encontrado con ID: " + id);

    private static VideoReelDTO ToDTO(VideoReel v)
    {
        var p = v.Product;
        return new VideoReelDTO
        {
            Id = v.Id,
            VideoUrl = v.VideoUrl,
            ThumbnailUrl = v.ThumbnailUrl,
            Username = v.Username,
            ProductId = p.Id,
            ProductTitle = p.Title,
            ProductPrice = JavaFormat.BigDecimal(p.Price),
            ProductImage = p.FirstImageOfSet()?.Image,
            ProductSlug = p.Slug,
            CreatedAt = JavaFormat.LocalDateTimeToString(v.CreatedAt),
        };
    }
}

/// <summary>Cálculo de las cifras del panel: ingresos, pedidos, ventas por día y productos más vendidos.</summary>
public sealed class ReportService(KioglossDbContext db)
{
    /// <summary>Resumen general: KPIs principales del dashboard.</summary>
    public async Task<ReportSummaryDTO> GetSummary()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var totalRevenue = await db.Orders.SumAsync(o => (double?)o.Amount) ?? 0.0;
        var totalOrders = await db.Orders.LongCountAsync();
        var activeProducts = await db.Products.LongCountAsync(p => p.Status == "published");
        var revenueToday = await db.Orders.Where(o => o.Date == today).SumAsync(o => (double?)o.Amount) ?? 0.0;
        var ordersToday = await db.Orders.LongCountAsync(o => o.Date == today);
        return new ReportSummaryDTO(totalRevenue, totalOrders, activeProducts, revenueToday, ordersToday);
    }

    /// <summary>Ventas agrupadas por día (últimos N días).</summary>
    public async Task<List<SalesByDayDTO>> GetSalesByDay(int days)
    {
        var startDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-days);
        return (await db.Orders
                .Where(o => o.Date >= startDate)
                .GroupBy(o => o.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(o => (double?)o.Amount), Count = g.LongCount() })
                .OrderBy(x => x.Date)
                .ToListAsync())
            .Select(x => new SalesByDayDTO(x.Date, x.Total, x.Count))
            .ToList();
    }

    /// <summary>Top N productos más vendidos por cantidad.</summary>
    public async Task<List<TopProductDTO>> GetTopProducts(int limit)
    {
        var pageable = PageRequest.Of(0, limit);
        return (await db.DetailOrders
                .GroupBy(d => new { d.Variant.Product.Id, d.Variant.Product.Title })
                .Select(g => new
                {
                    g.Key.Id,
                    g.Key.Title,
                    Quantity = g.Sum(d => (long?)d.Quantity),
                    Revenue = g.Sum(d => (double?)(d.Price * d.Quantity)),
                })
                .OrderByDescending(x => x.Quantity)
                .Take(pageable.PageSize)
                .ToListAsync())
            .Select(x => new TopProductDTO(x.Id, x.Title, x.Quantity, x.Revenue))
            .ToList();
    }

    /// <summary>Distribución de órdenes por estado (status = enum name, as OrderStatus#toString).</summary>
    public async Task<List<OrderStatusDistributionDTO>> GetOrderStatusDistribution()
    {
        // grouped by the raw column, each value converted like OrderStatusConverter does
        var list = new List<OrderStatusDistributionDTO>();
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT o.status, COUNT(o.id) FROM store_order o GROUP BY o.status";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var status = reader.IsDBNull(0) ? null : reader.GetString(0);
                list.Add(new OrderStatusDistributionDTO(
                    status != null ? OrderStatusExtensions.FromDatabase(status).ToString() : "UNKNOWN", reader.GetInt64(1)));
            }
        }
        finally
        {
            if (wasClosed) await db.Database.CloseConnectionAsync();
        }

        var total = list.Sum(i => i.Count);
        foreach (var item in list)
            item.Percentage = total > 0 ? JavaRound(item.Count * 100.0 / total * 100.0) / 100.0 : 0.0;
        return list;
    }

    /// <summary>java.lang.Math#round(double) (half up), not .NET's banker's rounding.</summary>
    private static double JavaRound(double value) => Math.Floor(value + 0.5);
}

internal static class ExpressionExtensions
{
    public static Expression<Func<T, bool>> OrElse<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var body = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
