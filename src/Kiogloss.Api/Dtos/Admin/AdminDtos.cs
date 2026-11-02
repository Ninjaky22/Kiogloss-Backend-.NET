// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Lo mismo que Dtos.cs pero para el panel de administración (productos, usuarios, pedidos, estadísticas).
// ======================================

using System.Text.Json.Serialization;
using Kiogloss.Api.Compat;

// Port of com.todoteg.dto.admin (only the classes the endpoints actually use).
namespace Kiogloss.Api.Dtos.Admin;

public sealed class ProductAdminResponse
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("published")] public string? Published { get; set; }
    /// <summary>Stock from default variant (when no real variants)</summary>
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("attributes")] public Dictionary<string, string?>? Attributes { get; set; }
    [JsonPropertyName("variants")] public List<ProductVariantDTO>? Variants { get; set; }
    [JsonPropertyName("tags")] public List<TagDTO>? Tags { get; set; }
    [JsonPropertyName("images")] public List<ImageDTO>? Images { get; set; }
}

public sealed class ProductVariantRequestDTO
{
    /// <summary>Optional for creation, required for update</summary>
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("optionIds")] public List<long?>? OptionIds { get; set; }
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("sku")] public string? Sku { get; set; }
    [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
}

public sealed class ProductCreateRequest
{
    [NotBlank][JsonPropertyName("title")] public string? Title { get; set; }
    [NotNull][JsonPropertyName("price")] public decimal? Price { get; set; }
    [NotBlank][JsonPropertyName("description")] public string? Description { get; set; }
    [NotBlank][JsonPropertyName("slug")] public string? Slug { get; set; }
    /// <summary>"draft" o "published"</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }
    /// <summary>Used when no variants are provided (default variant)</summary>
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("attributes")] public Dictionary<string, string?>? Attributes { get; set; }
    [JsonPropertyName("variants")] public List<ProductVariantRequestDTO?>? Variants { get; set; }
    [JsonPropertyName("tagIds")] public List<long?>? TagIds { get; set; }
}

public sealed class ProductUpdateRequest
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("attributes")] public Dictionary<string, string?>? Attributes { get; set; }
    [JsonPropertyName("variants")] public List<ProductVariantRequestDTO?>? Variants { get; set; }
    [JsonPropertyName("tagIds")] public List<long?>? TagIds { get; set; }
}

public sealed class ProductImageUploadRequest
{
    [NotNull][JsonPropertyName("productId")] public long? ProductId { get; set; }
    /// <summary>Imagen en Base64</summary>
    [NotBlank][JsonPropertyName("imageBase64")] public string? ImageBase64 { get; set; }
}

public sealed class TagCreateRequest
{
    [NotBlank][JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("imageURL")] public string? ImageURL { get; set; }
}

public sealed class TagUpdateRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("imageURL")] public string? ImageURL { get; set; }
}

public sealed class VariantOptionCreateRequest
{
    /// <summary>null for new options, present for existing ones (used in update)</summary>
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("metaValue")] public string? MetaValue { get; set; }
}

public sealed class VariantTypeCreateRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>e.g. "button", "color"</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("options")] public List<VariantOptionCreateRequest?>? Options { get; set; }
}

public sealed class UpdateUserRolesDTO
{
    [JsonPropertyName("userId")] public long? UserId { get; set; }
    [JsonPropertyName("isStaff")] public bool? IsStaff { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
}

public sealed class AccountSummaryDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("pointsPerPurchase")] public int? PointsPerPurchase { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
    [JsonPropertyName("address")] public AddressDTO? Address { get; set; }
}

public sealed class AccountDetailDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("pointsPerPurchase")] public int? PointsPerPurchase { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
    [JsonPropertyName("address")] public AddressDTO? Address { get; set; }
    [JsonPropertyName("totalOrders")] public int? TotalOrders { get; set; }
    [JsonPropertyName("totalFavorites")] public int? TotalFavorites { get; set; }
}

public sealed class UserDashboardDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
    [JsonPropertyName("isStaff")] public bool? IsStaff { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; }
    /// <summary>Never filled by UserMapper — always null.</summary>
    [JsonPropertyName("account")] public AccountSummaryDTO? Account { get; set; }
    [JsonPropertyName("totalOrders")] public int? TotalOrders { get; set; }
    [JsonPropertyName("totalFavorites")] public int? TotalFavorites { get; set; }
}

public sealed class UserDetailDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
    [JsonPropertyName("isStaff")] public bool? IsStaff { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; }
    [JsonPropertyName("totalOrders")] public int? TotalOrders { get; set; }
    [JsonPropertyName("totalFavorites")] public int? TotalFavorites { get; set; }
    [JsonPropertyName("account")] public AccountDetailDTO? Account { get; set; }
    [JsonPropertyName("recentOrders")] public List<OrderSummaryDTO>? RecentOrders { get; set; }
}

public sealed class CustomerBasicDTO
{
    [JsonPropertyName("userId")] public long? UserId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
}

public sealed class OrderSummaryDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("amount")] public double? Amount { get; set; }
    [JsonPropertyName("date")] public DateOnly? Date { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("itemCount")] public int? ItemCount { get; set; }
    [JsonPropertyName("shippingAddress")] public string? ShippingAddress { get; set; }
    [JsonPropertyName("customer")] public CustomerBasicDTO? Customer { get; set; }
}

public sealed class ProductBasicDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    /// <summary>Primera imagen del producto</summary>
    [JsonPropertyName("firstImage")] public string? FirstImage { get; set; }
}

public sealed class OrderItemDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("price")] public double? Price { get; set; }
    [JsonPropertyName("product")] public ProductBasicDTO? Product { get; set; }
}

public sealed class OrderDetailDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("amount")] public double? Amount { get; set; }
    [JsonPropertyName("date")] public DateOnly? Date { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("shippingAddress")] public string? ShippingAddress { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("customer")] public CustomerBasicDTO? Customer { get; set; }
    [JsonPropertyName("items")] public List<OrderItemDTO>? Items { get; set; }
}

public sealed class PageResponseDTO<T>(List<T> content, int pageNumber, int pageSize, long totalElements, int totalPages, bool last)
{
    [JsonPropertyName("content")] public List<T> Content { get; } = content;
    [JsonPropertyName("pageNumber")] public int PageNumber { get; } = pageNumber;
    [JsonPropertyName("pageSize")] public int PageSize { get; } = pageSize;
    [JsonPropertyName("totalElements")] public long TotalElements { get; } = totalElements;
    [JsonPropertyName("totalPages")] public int TotalPages { get; } = totalPages;
    [JsonPropertyName("last")] public bool Last { get; } = last;

    public static PageResponseDTO<T> From<TIn>(SpringPage<TIn> page, Func<TIn, T> mapper) =>
        new(page.Content.Select(mapper).ToList(), page.Number, page.Size, page.TotalElements, page.TotalPages, page.Last);
}

public sealed class DashboardStatsDTO
{
    [JsonPropertyName("totalUsers")] public long? TotalUsers { get; set; }
    [JsonPropertyName("activeUsers")] public long? ActiveUsers { get; set; }
    [JsonPropertyName("totalOrders")] public long? TotalOrders { get; set; }
    [JsonPropertyName("pendingOrders")] public long? PendingOrders { get; set; }
    [JsonPropertyName("totalRevenue")] public double? TotalRevenue { get; set; }
    [JsonPropertyName("newUsersThisMonth")] public long? NewUsersThisMonth { get; set; }
}
