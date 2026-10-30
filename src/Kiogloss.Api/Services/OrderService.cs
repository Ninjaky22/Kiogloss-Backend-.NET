using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Services;

/// <summary>Port of com.todoteg.service.OrderService.</summary>
public sealed class OrderService(
    KioglossDbContext db,
    CloudinaryService cloudinaryService,
    NotificationService notificationService)
{
    public async Task CreateOrder(OrderCreateRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var account = await db.Accounts.FindAsync(request.Account!.Value) ?? throw new Compat.RuntimeException("Account not found");

        // Dirección de entrega: la enviada en el checkout o, si falta, la del perfil
        var shippingAddress = !string.IsNullOrWhiteSpace(request.ShippingAddress)
            ? request.ShippingAddress.Trim()
            : account.Address?.ToString();
        if (string.IsNullOrWhiteSpace(shippingAddress))
            throw new Compat.IllegalArgumentException("La dirección de entrega es obligatoria");
        if (shippingAddress.Length > 255) shippingAddress = shippingAddress[..255];

        // Se valida el estado recibido (contrato de la API), pero todo pedido nuevo inicia en "Pendiente"
        _ = OrderStatusExtensions.ValueOf(request.Status!.ToUpper());

        var order = new Order
        {
            Account = account,
            Amount = request.Amount!.Value,
            Status = OrderStatus.PENDING,
            Date = DateOnly.FromDateTime(DateTime.Now),
            ShippingAddress = shippingAddress,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        try
        {
            await notificationService.NotifyOrderCreated(order.Id, account.User.Name, order.Amount);
        }
        catch (Exception)
        {
            // best-effort
        }

        // Crear detalles de orden
        foreach (var detail in request.Shopping!)
        {
            if (detail == null) throw JavaErrors.NullPointer();
            if (detail.Product == null) throw JavaErrors.InvalidDataAccessApiUsage("The given id must not be null");
            var variant = await db.ProductVariants.FindAsync(detail.Product.Value)
                          ?? throw new Compat.RuntimeException("Variant not found");

            var quantity = detail.Quantity ?? throw JavaErrors.NullPointer();
            if (variant.Stock < quantity)
            {
                throw new Compat.RuntimeException(
                    "Stock insuficiente para el producto: " + variant.Product.Title +
                    ". Disponible: " + variant.Stock +
                    ", Solicitado: " + quantity);
            }

            // Actualizar stock de la variante
            variant.Stock -= quantity;
            await db.SaveChangesAsync();

            try
            {
                await notificationService.CheckAndNotifyStock(variant.Id, variant.Stock, variant.Product.Title);
            }
            catch (Exception)
            {
                // best-effort: no afecta la creación de la orden
            }

            db.DetailOrders.Add(new DetailOrder
            {
                Order = order,
                Variant = variant,
                Quantity = quantity,
                Price = detail.Price ?? throw JavaErrors.NullPointer(),
            });
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<SpringPage<OrderDetailResponse>> GetUserOrders(long userId, OrderStatus? status,
        DateOnly? startDate, DateOnly? endDate, PageRequest pageable)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == userId)
                      ?? throw new Compat.RuntimeException("Account not found");

        // Inherited behaviour: Hibernate binds the LocalDate parameters of "(:startDate IS NULL OR ...)" untyped and
        // PostgreSQL rejects the statement, so in the Spring backend every date-filtered request ends in HTTP 500.
        // Reproduced on purpose (see docs/SECURITY-FINDINGS.md, "Bugs heredados").
        if (startDate != null || endDate != null)
        {
            var pagination = pageable.Offset > 0 ? "offset ? rows fetch first ? rows only" : "fetch first ? rows only";
            var parameter = startDate != null ? "$4" : "$6";
            throw new JavaException("org.springframework.dao.InvalidDataAccessResourceUsageException",
                "JDBC exception executing SQL [select o1_0.id,o1_0.account_id,o1_0.amount,o1_0.date,o1_0.status from store_order o1_0 " +
                "where o1_0.account_id=? and (? is null or o1_0.status=?) and (? is null or o1_0.date>=?) and (? is null or o1_0.date<=?) " +
                $"order by o1_0.id desc {pagination}] [ERROR: could not determine data type of parameter {parameter}] [n/a]; SQL [n/a]");
        }

        var query = db.Orders.Where(o => o.AccountId == account.Id);
        if (status != null) query = query.Where(o => o.Status == status.Value);
        if (startDate != null) query = query.Where(o => o.Date >= startDate.Value);
        if (endDate != null) query = query.Where(o => o.Date <= endDate.Value);
        var orders = await query.OrderByDescending(o => o.Id).ToSpringPageAsync(pageable);

        var content = new List<OrderDetailResponse>();
        foreach (var order in orders.Content) content.Add(await ConvertToOrderDetailResponse(order));
        return new SpringPage<OrderDetailResponse>(content, pageable, orders.TotalElements);
    }

    private async Task<OrderDetailResponse> ConvertToOrderDetailResponse(Order order)
    {
        var details = await db.DetailOrders.Where(d => d.OrderId == order.Id).ToListAsync();

        var shoppingItems = details.Select(detail =>
        {
            var variant = detail.Variant;
            var product = variant.Product;
            string? image = null;
            if (variant.ImageUrl != null)
                image = variant.ImageUrl;
            else if (product.Images.Count > 0)
                image = cloudinaryService.GetImageUrl(product.FirstImageOfSet()!.Image);

            return new ShoppingItemDTO
            {
                Title = product.Title,
                Slug = product.Slug,
                Image = image,
                Price = JavaFormat.BigDecimal(product.Price),
                Quantity = detail.Quantity,
                // Using size as a generic label for variant details
                Size = variant.OptionsLabel(),
                PriceXquantity = detail.Price,
            };
        }).ToList();

        var user = order.Account.User;
        return new OrderDetailResponse
        {
            Id = order.Id,
            Shopping = shoppingItems,
            User = new OrderUserDTO
            {
                Name = user.Name,
                PhoneNumber = user.PhoneNumber,
                Address = Mappers.UserMapper.ResolveShippingAddress(order) ?? "",
            },
            Amount = order.Amount,
            Date = order.Date,
            Status = order.Status.Description(),
            ShippingAddress = Mappers.UserMapper.ResolveShippingAddress(order) ?? "",
        };
    }

    /// <summary>Buscar todas las órdenes con filtros opcionales (sorted by date DESC).</summary>
    public Task<SpringPage<Order>> FindAllOrders(string? statusStr, long? userId, PageRequest pageable)
    {
        OrderStatus? status = null;
        if (!string.IsNullOrEmpty(statusStr) && statusStr != "ALL")
            status = OrderStatusExtensions.ValueOf(statusStr.ToUpper());

        IQueryable<Order> query = db.Orders;
        if (status != null) query = query.Where(o => o.Status == status.Value);
        if (userId != null) query = query.Where(o => o.Account.UserId == userId.Value);
        // Date no tiene hora: el Id desempata para que el orden sea estable entre recargas y páginas
        return query.OrderByDescending(o => o.Date).ThenByDescending(o => o.Id).ToSpringPageAsync(pageable);
    }

    public async Task<Order> FindById(long id) =>
        await db.Orders.FindAsync(id) ?? throw new ResourceNotFoundException("Orden no encontrada con ID: " + id);

    /// <summary>Buscar órdenes de un usuario específico.</summary>
    public async Task<SpringPage<Order>> FindOrdersByUserId(long userId, PageRequest pageable)
    {
        // Verificar que el usuario existe
        _ = await db.Users.FindAsync(userId) ?? throw new ResourceNotFoundException("Usuario no encontrado con ID: " + userId);
        return await db.Orders.Where(o => o.Account.UserId == userId).OrderByDescending(o => o.Date).ThenByDescending(o => o.Id).ToSpringPageAsync(pageable);
    }

    /// <summary>Actualizar estado de una orden.</summary>
    public async Task<Order> UpdateOrderStatus(long id, string statusStr)
    {
        var order = await FindById(id);

        OrderStatus status;
        try
        {
            status = OrderStatusExtensions.ValueOf(statusStr.ToUpper());
        }
        catch (Compat.IllegalArgumentException)
        {
            throw new Compat.IllegalArgumentException("Estado inválido: " + statusStr);
        }

        var previousStatus = order.Status;
        order.Status = status;
        await db.SaveChangesAsync();

        // "Pendiente" es el estado inicial: no se notifica al cliente; tampoco si el estado no cambió
        if (status == OrderStatus.PENDING || status == previousStatus) return order;

        try
        {
            await notificationService.NotifyOrderStatusChanged(order.Id, order.Status, order.Account.UserId);
        }
        catch (Exception)
        {
            // best-effort
        }
        return order;
    }
}
