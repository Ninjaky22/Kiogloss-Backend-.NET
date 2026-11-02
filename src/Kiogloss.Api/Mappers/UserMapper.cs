// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Toma los datos tal como están en la base de datos y los transforma en lo que necesita mostrar la pantalla.
// ======================================

using Kiogloss.Api.Compat;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Dtos.Admin;
using Kiogloss.Api.Models;

namespace Kiogloss.Api.Mappers;

/// <summary>Transforma los datos de usuarios y pedidos al formato que necesitan mostrar el panel admin y el usuario.</summary>
public sealed class UserMapper
{
    public UserDashboardDTO ToUserDashboardDTO(UserProfile user)
    {
        var dto = new UserDashboardDTO
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            PhoneNumber = user.PhoneNumber,
            ProfileImage = user.ProfileImage,
            IsActive = user.IsActive,
            IsStaff = user.IsStaff,
            IsSuperuser = user.IsSuperuser,
        };
        if (user.Account != null)
        {
            dto.TotalOrders = user.Account.Orders.Count;
            dto.TotalFavorites = user.Account.Favorites.Count;
        }
        return dto;
    }

    public UserDetailDTO ToUserDetailDTO(UserProfile user)
    {
        var dto = new UserDetailDTO
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            PhoneNumber = user.PhoneNumber,
            ProfileImage = user.ProfileImage,
            IsActive = user.IsActive,
            IsStaff = user.IsStaff,
            IsSuperuser = user.IsSuperuser,
        };

        if (user.Account != null)
        {
            var account = user.Account;
            // Contadores a nivel raíz (lo que el frontend espera como user.totalFavorites)
            var favCount = account.Favorites.Count;
            var ordCount = account.Orders.Count;
            dto.TotalFavorites = favCount;
            dto.TotalOrders = ordCount;

            dto.Account = new AccountDetailDTO
            {
                Id = account.Id,
                PointsPerPurchase = account.PointsPerPurchase,
                IsActive = account.IsActive,
                Address = ToAddressDTO(account.Address),
                TotalOrders = ordCount,
                TotalFavorites = favCount,
            };

            // Últimas 5 órdenes ordenadas por fecha descendente
            dto.RecentOrders = account.Orders
                .OrderByDescending(o => o.Date)
                .ThenByDescending(o => o.Id)
                .Take(5)
                .Select(ToOrderSummaryDTO)
                .ToList();
        }
        else
        {
            dto.TotalFavorites = 0;
            dto.TotalOrders = 0;
        }
        return dto;
    }

    public CustomerBasicDTO ToCustomerBasicDTO(UserProfile user) => new()
    {
        UserId = user.Id,
        Name = user.Name,
        Email = user.Email,
        ProfileImage = user.ProfileImage,
    };

    public OrderSummaryDTO ToOrderSummaryDTO(Order order) => new()
    {
        Id = order.Id,
        Amount = order.Amount,
        Date = order.Date,
        Status = order.Status.Description(),
        ItemCount = order.Shopping.Count,
        ShippingAddress = ResolveShippingAddress(order),
        Customer = order.Account?.User != null ? ToCustomerBasicDTO(order.Account.User) : null,
    };

    public OrderDetailDTO ToOrderDetailDTO(Order order) => new()
    {
        Id = order.Id,
        Amount = order.Amount,
        Date = order.Date,
        Status = order.Status.Description(),
        ShippingAddress = ResolveShippingAddress(order),
        PhoneNumber = order.Account?.User?.PhoneNumber,
        Customer = order.Account?.User != null ? ToCustomerBasicDTO(order.Account.User) : null,
        Items = order.Shopping.OrderBy(d => d.Id).Select(detail => new OrderItemDTO
        {
            Id = detail.Id,
            Quantity = detail.Quantity,
            Price = detail.Price,
            Size = detail.Variant.OptionsLabel(),
            Color = "",
            Product = new ProductBasicDTO
            {
                Id = detail.Variant.Product.Id,
                Title = detail.Variant.Product.Title,
                Slug = detail.Variant.Product.Slug,
                FirstImage = detail.Variant.ImageUrl ?? detail.Variant.Product.FirstImageOfSet()?.Image,
            },
        }).ToList(),
    };

    /// <summary>Dirección guardada en la orden; para órdenes antiguas usa la dirección del perfil.</summary>
    public static string? ResolveShippingAddress(Order order) =>
        !string.IsNullOrWhiteSpace(order.ShippingAddress) ? order.ShippingAddress : order.Account?.Address?.ToString();

    public PageResponseDTO<OrderSummaryDTO> ToOrderSummaryPage(SpringPage<Order> page) =>
        PageResponseDTO<OrderSummaryDTO>.From(page, ToOrderSummaryDTO);

    public PageResponseDTO<UserDashboardDTO> ToUserDashboardPage(SpringPage<UserProfile> page) =>
        PageResponseDTO<UserDashboardDTO>.From(page, ToUserDashboardDTO);

    public AddressDTO? ToAddressDTO(Address? address) => address == null
        ? null
        : new AddressDTO
        {
            Id = address.Id,
            Street = address.Street,
            StreetNumber = address.StreetNumber,
            Distric = address.Distric,
        };
}
