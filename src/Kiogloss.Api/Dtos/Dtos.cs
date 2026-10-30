using System.Text.Json.Serialization;
using Kiogloss.Api.Compat;

// Port of com.todoteg.dto. Property order = the order Jackson wrote them (fields carrying
// @JsonProperty in Java are emitted last), so the JSON is byte-for-byte the same.
namespace Kiogloss.Api.Dtos;

// ============================================================== auth

public sealed class TokenResponse(string access, string refresh)
{
    [JsonPropertyName("access")] public string Access { get; } = access;
    [JsonPropertyName("refresh")] public string Refresh { get; } = refresh;
}

public sealed class LoginRequest
{
    [Email, NotBlank] [JsonPropertyName("email")] public string? Email { get; set; }
    [NotBlank] [JsonPropertyName("password")] public string? Password { get; set; }
}

public sealed class RefreshTokenRequest
{
    [JsonPropertyName("refresh")] public string? Refresh { get; set; }
}

public sealed class ForgotPasswordRequest
{
    [NotBlank(Message = "El email es obligatorio"), Email(Message = "Email inválido")]
    [JsonPropertyName("email")] public string? Email { get; set; }
}

public sealed class ResetPasswordRequest
{
    [NotBlank(Message = "El token es obligatorio")]
    [JsonPropertyName("token")] public string? Token { get; set; }

    [NotBlank(Message = "La nueva contraseña es obligatoria"), Size(min: 8, Message = "La contraseña debe tener al menos 8 caracteres")]
    [JsonPropertyName("newPassword")] public string? NewPassword { get; set; }
}

// ============================================================== users

public sealed class AccountDTO
{
    [JsonPropertyName("pointsPerPurchase")] public int? PointsPerPurchase { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
}

public sealed class AddressDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("street")] public string? Street { get; set; }
    [JsonPropertyName("streetNumber")] public string? StreetNumber { get; set; }
    [JsonPropertyName("distric")] public string? Distric { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }

    /// <summary>getFullAddress() is a bean getter, so Jackson serializes it too.</summary>
    [JsonPropertyName("fullAddress")]
    public string FullAddress =>
        JavaFormat.Str(Street) + " " + JavaFormat.Str(StreetNumber) + ", " + JavaFormat.Str(Distric) + (City != null ? ", " + City : "");
}

public sealed class UserCreateRequest
{
    [Email, NotBlank] [JsonPropertyName("email")] public string? Email { get; set; }
    [NotBlank] [JsonPropertyName("name")] public string? Name { get; set; }
    [NotBlank] [JsonPropertyName("password")] public string? Password { get; set; }
    /// <summary>Base64</summary>
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("account")] public AccountDTO? Account { get; set; }
    [NotNull] [JsonPropertyName("address")] public AddressDTO? Address { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; } = false;
}

public sealed class UserUpdateRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    /// <summary>Base64</summary>
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("address")] public AddressDTO? Address { get; set; }
}

public sealed class AccountInfoDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    /// <summary>List of FavoriteProductDTO or the string "No tienes favoritos".</summary>
    [JsonPropertyName("favorite")] public object? Favorite { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("pointsPerPurchase")] public int? PointsPerPurchase { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
}

public sealed class UserDetailResponse
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; }
    [JsonPropertyName("authorities")] public IReadOnlyList<string> Authorities { get; set; } = [];
    [JsonPropertyName("account")] public AccountInfoDTO? Account { get; set; }
}

/// <summary>The UserProfile entity as Jackson serialized it (GET /user/), UserDetails getters included.</summary>
public sealed class UserProfileJson
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
    [JsonPropertyName("isStaff")] public bool? IsStaff { get; set; }
    [JsonPropertyName("isSuperuser")] public bool? IsSuperuser { get; set; }
    [JsonPropertyName("profileImage")] public string? ProfileImage { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("account")] public AccountJson? Account { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("authorities")] public List<AuthorityJson> Authorities { get; set; } = [];
    [JsonPropertyName("accountNonExpired")] public bool AccountNonExpired => true;
    [JsonPropertyName("accountNonLocked")] public bool AccountNonLocked => true;
    [JsonPropertyName("credentialsNonExpired")] public bool CredentialsNonExpired => true;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public sealed class AccountJson
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("address")] public AddressJson? Address { get; set; }
    [JsonPropertyName("pointsPerPurchase")] public int? PointsPerPurchase { get; set; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
}

public sealed class AddressJson
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("street")] public string? Street { get; set; }
    [JsonPropertyName("streetNumber")] public string? StreetNumber { get; set; }
    [JsonPropertyName("distric")] public string? Distric { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class AuthorityJson(string authority)
{
    [JsonPropertyName("authority")] public string Authority { get; } = authority;
}

// ============================================================== favorites

public sealed class FavoriteCreateRequest
{
    [NotNull] [JsonPropertyName("account")] public long? Account { get; set; }
    [NotNull] [JsonPropertyName("product")] public long? Product { get; set; }
}

public sealed class FavoriteIdDTO(long id, long product)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    [JsonPropertyName("product")] public long Product { get; } = product;
}

public sealed class FavoriteProductDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("images")] public List<string?>? Images { get; set; }
    [JsonPropertyName("hasVariants")] public bool HasVariants { get; set; }
    [JsonPropertyName("defaultVariantId")] public long? DefaultVariantId { get; set; }
    [JsonPropertyName("idFa")] public long? IdFa { get; set; }
}

public sealed class FavoriteProductItemDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("defaultVariantId")] public long? DefaultVariantId { get; set; }
}

public sealed class AccountFavoritesDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("favoriteID")] public List<FavoriteIdDTO>? FavoriteID { get; set; }
    /// <summary>List of FavoriteProductItemDTO or the string "No tienes favoritos".</summary>
    [JsonPropertyName("favorite")] public object? Favorite { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class AccountFavoritesResponse(AccountFavoritesDTO account)
{
    [JsonPropertyName("account")] public AccountFavoritesDTO Account { get; } = account;
}

// ============================================================== catalog

public sealed class TagDTO(long id, string? name, string? imageURL)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    [JsonPropertyName("name")] public string? Name { get; } = name;
    [JsonPropertyName("imageURL")] public string? ImageURL { get; } = imageURL;
}

public sealed class ImageDTO(long id, string? url)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    [JsonPropertyName("url")] public string? Url { get; } = url;
}

public sealed class VariantOptionDTO(long id, string? typeName, string? value, string? metaValue)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    /// <summary>e.g. "Color", "Talla", "Modelo"</summary>
    [JsonPropertyName("typeName")] public string? TypeName { get; } = typeName;
    [JsonPropertyName("value")] public string? Value { get; } = value;
    [JsonPropertyName("metaValue")] public string? MetaValue { get; } = metaValue;
}

public sealed class VariantTypeDTO(long id, string? name, string? type, List<VariantOptionDTO> options)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    [JsonPropertyName("name")] public string? Name { get; } = name;
    [JsonPropertyName("type")] public string? Type { get; } = type;
    [JsonPropertyName("options")] public List<VariantOptionDTO> Options { get; } = options;
}

public sealed class ProductVariantDTO(long id, List<VariantOptionDTO> options, int stock, string? sku, string? imageUrl)
{
    [JsonPropertyName("id")] public long Id { get; } = id;
    [JsonPropertyName("options")] public List<VariantOptionDTO> Options { get; } = options;
    [JsonPropertyName("stock")] public int Stock { get; } = stock;
    [JsonPropertyName("sku")] public string? Sku { get; } = sku;
    [JsonPropertyName("imageUrl")] public string? ImageUrl { get; } = imageUrl;
}

public sealed class ProductListDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("hasVariants")] public bool HasVariants { get; set; }
    /// <summary>ID of the single default variant (for direct add-to-cart when !hasVariants)</summary>
    [JsonPropertyName("defaultVariantId")] public long? DefaultVariantId { get; set; }
}

public sealed class ProductDetailDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("images")] public List<string?>? Images { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("attributes")] public Dictionary<string, string?>? Attributes { get; set; }
    [JsonPropertyName("variants")] public List<ProductVariantDTO>? Variants { get; set; }
}

// ============================================================== videos

public sealed class VideoReelDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }
    [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("productId")] public long? ProductId { get; set; }
    [JsonPropertyName("productTitle")] public string? ProductTitle { get; set; }
    [JsonPropertyName("productPrice")] public string? ProductPrice { get; set; }
    [JsonPropertyName("productImage")] public string? ProductImage { get; set; }
    [JsonPropertyName("productSlug")] public string? ProductSlug { get; set; }
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
}

public sealed class VideoCreateRequest
{
    [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }
    [NotBlank] [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
    [NotBlank] [JsonPropertyName("username")] public string? Username { get; set; }
    [NotNull] [JsonPropertyName("productId")] public long? ProductId { get; set; }
}

public sealed class VideoUpdateRequest
{
    [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }
    [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("productId")] public long? ProductId { get; set; }
}

// ============================================================== notifications

public sealed class NotificationDTO
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("type")] public Models.NotificationType? Type { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("relatedEntityType")] public string? RelatedEntityType { get; set; }
    [JsonPropertyName("relatedEntityId")] public long? RelatedEntityId { get; set; }
    [JsonPropertyName("read")] public bool Read { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
}

// ============================================================== orders

public sealed class DetailProductRequest
{
    /// <summary>Despite the name, this is a ProductVariant id.</summary>
    [NotNull] [JsonPropertyName("product")] public long? Product { get; set; }
    [NotNull] [JsonPropertyName("quantity")] public int? Quantity { get; set; }
    [NotNull] [JsonPropertyName("price")] public double? Price { get; set; }
}

public sealed class OrderCreateRequest
{
    [NotNull] [JsonPropertyName("account")] public long? Account { get; set; }
    [NotNull] [JsonPropertyName("shopping")] public List<DetailProductRequest?>? Shopping { get; set; }
    [NotNull] [JsonPropertyName("amount")] public double? Amount { get; set; }
    [NotNull] [JsonPropertyName("status")] public string? Status { get; set; }
    /// <summary>Dirección de entrega; si viene vacía se usa la dirección del perfil.</summary>
    [JsonPropertyName("shippingAddress")] public string? ShippingAddress { get; set; }
}

public sealed class ShoppingItemDTO
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("priceXquantity")] public double? PriceXquantity { get; set; }
}

public sealed class OrderUserDTO
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
}

public sealed class OrderDetailResponse
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("shopping")] public List<ShoppingItemDTO>? Shopping { get; set; }
    [JsonPropertyName("user")] public OrderUserDTO? User { get; set; }
    [JsonPropertyName("amount")] public double? Amount { get; set; }
    [JsonPropertyName("date")] public DateOnly? Date { get; set; }
    /// <summary>OrderStatus serialized through @JsonValue (description).</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("shippingAddress")] public string? ShippingAddress { get; set; }
}

// ============================================================== reports

public sealed class ReportSummaryDTO(double? totalRevenue, long? totalOrders, long? activeProducts, double? revenueToday, long? ordersToday)
{
    [JsonPropertyName("totalRevenue")] public double? TotalRevenue { get; } = totalRevenue;
    [JsonPropertyName("totalOrders")] public long? TotalOrders { get; } = totalOrders;
    [JsonPropertyName("activeProducts")] public long? ActiveProducts { get; } = activeProducts;
    [JsonPropertyName("revenueToday")] public double? RevenueToday { get; } = revenueToday;
    [JsonPropertyName("ordersToday")] public long? OrdersToday { get; } = ordersToday;
}

public sealed class SalesByDayDTO(DateOnly date, double? totalAmount, long totalOrders)
{
    [JsonPropertyName("date")] public DateOnly Date { get; } = date;
    [JsonPropertyName("totalAmount")] public double? TotalAmount { get; } = totalAmount;
    [JsonPropertyName("totalOrders")] public long TotalOrders { get; } = totalOrders;
}

public sealed class TopProductDTO(long productId, string? productTitle, long? totalQuantitySold, double? totalRevenue)
{
    [JsonPropertyName("productId")] public long ProductId { get; } = productId;
    [JsonPropertyName("productTitle")] public string? ProductTitle { get; } = productTitle;
    [JsonPropertyName("totalQuantitySold")] public long? TotalQuantitySold { get; } = totalQuantitySold;
    [JsonPropertyName("totalRevenue")] public double? TotalRevenue { get; } = totalRevenue;
}

public sealed class OrderStatusDistributionDTO(string status, long count)
{
    [JsonPropertyName("status")] public string Status { get; } = status;
    [JsonPropertyName("count")] public long Count { get; } = count;
    [JsonPropertyName("percentage")] public double? Percentage { get; set; }
}
