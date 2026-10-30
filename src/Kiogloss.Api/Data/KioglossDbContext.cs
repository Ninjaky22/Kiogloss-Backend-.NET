using Kiogloss.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Data;

/// <summary>
/// Maps the existing schema created by Hibernate (see db/schema.sql). Nothing here changes the
/// database: no migrations run at startup and every table/column name is explicit.
/// </summary>
public class KioglossDbContext(DbContextOptions<KioglossDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> Users => Set<UserProfile>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<DetailFavoritos> Favorites => Set<DetailFavoritos>();
    public DbSet<DetailOrder> DetailOrders => Set<DetailOrder>();
    public DbSet<Images> Images => Set<Images>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<VariantOption> VariantOptions => Set<VariantOption>();
    public DbSet<VariantType> VariantTypes => Set<VariantType>();
    public DbSet<VideoReel> VideoReels => Set<VideoReel>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<UserProfile>(e =>
        {
            e.ToTable("store_userprofile");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Password).HasColumnName("password").HasMaxLength(255).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.IsStaff).HasColumnName("is_staff");
            e.Property(x => x.IsSuperuser).HasColumnName("is_superuser");
            e.Property(x => x.ProfileImage).HasColumnName("profile_image").HasMaxLength(255);
            e.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(255);
            e.HasOne(x => x.Account).WithOne(a => a.User).HasForeignKey<Account>(a => a.UserId);
        });

        b.Entity<Account>(e =>
        {
            e.ToTable("store_account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.AddressId).HasColumnName("address_id");
            e.Property(x => x.PointsPerPurchase).HasColumnName("points_per_purchase");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasOne(x => x.Address).WithMany().HasForeignKey(x => x.AddressId);
            e.HasMany(x => x.Favorites).WithOne(f => f.Account).HasForeignKey(f => f.AccountId);
            e.HasMany(x => x.Orders).WithOne(o => o.Account).HasForeignKey(o => o.AccountId);
        });

        b.Entity<Address>(e =>
        {
            e.ToTable("store_address");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Street).HasColumnName("street").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.StreetNumber).HasColumnName("street_number").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.Distric).HasColumnName("distric").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(255);
        });

        b.Entity<CartItem>(e =>
        {
            e.ToTable("store_cart_item");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.VariantId).HasColumnName("variant_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DetailFavoritos>(e =>
        {
            e.ToTable("store_detail_favoritos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Status).HasColumnName("status");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DetailOrder>(e =>
        {
            e.ToTable("store_detail_order");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.VariantId).HasColumnName("variant_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.Price).HasColumnName("price");
            e.HasOne(x => x.Order).WithMany(o => o.Shopping).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Images>(e =>
        {
            e.ToTable("store_images");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Image).HasColumnName("image").HasMaxLength(255);
            // cascade = ALL on Product.images
            e.HasOne(x => x.Product).WithMany(p => p.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientCascade);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("store_notification");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.RecipientId).HasColumnName("recipient_user_id");
            e.Property(x => x.RecipientScope).HasColumnName("recipient_scope").HasMaxLength(10);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).HasConversion<string>();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
            e.Property(x => x.Message).HasColumnName("message").HasMaxLength(255);
            e.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(20);
            e.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id");
            e.Property(x => x.Read).HasColumnName("is_read");
            e.Property(x => x.ReadAt).HasColumnName("read_at").HasColumnType("timestamp(6) without time zone");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp(6) without time zone");
            e.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("store_order");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.Date).HasColumnName("date");
            // OrderStatusConverter: stored as the description ("Pendiente"), read leniently
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
                .HasConversion(s => s.Description(), db => OrderStatusExtensions.FromDatabase(db));
            e.Property(x => x.ShippingAddress).HasColumnName("shipping_address").HasMaxLength(255);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("store_password_reset_token");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Token).HasColumnName("token").HasMaxLength(255);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp(6) without time zone");
            e.Property(x => x.Used).HasColumnName("used");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("store_product");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(255);
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(10,2)");
            e.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(255);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(10);
            e.Property(x => x.Published).HasColumnName("published").HasColumnType("timestamp(6) without time zone");
            e.HasMany(x => x.AttributeRows).WithOne().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.ClientCascade);
            e.HasMany(x => x.Tags).WithMany(t => t.Products).UsingEntity<Dictionary<string, object>>(
                "store_product_tag",
                r => r.HasOne<Tag>().WithMany().HasForeignKey("tag_id").OnDelete(DeleteBehavior.Restrict),
                l => l.HasOne<Product>().WithMany().HasForeignKey("product_id").OnDelete(DeleteBehavior.ClientCascade),
                j => j.HasKey("product_id", "tag_id"));
        });

        b.Entity<ProductAttribute>(e =>
        {
            e.ToTable("store_product_attributes");
            e.HasKey(x => new { x.ProductId, x.Name });
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Name).HasColumnName("attribute_name").HasMaxLength(255);
            e.Property(x => x.Value).HasColumnName("attribute_value").HasMaxLength(255);
        });

        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("store_product_variant");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Stock).HasColumnName("stock");
            e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(255);
            e.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(255);
            // cascade = ALL, orphanRemoval = true on Product.variants
            e.HasOne(x => x.Product).WithMany(p => p.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientCascade);
            e.HasMany(x => x.Options).WithMany(o => o.Variants).UsingEntity<Dictionary<string, object>>(
                "store_product_variant_options",
                r => r.HasOne<VariantOption>().WithMany().HasForeignKey("option_id").OnDelete(DeleteBehavior.Restrict),
                l => l.HasOne<ProductVariant>().WithMany().HasForeignKey("variant_id").OnDelete(DeleteBehavior.ClientCascade),
                j => j.HasKey("variant_id", "option_id"));
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("store_tag");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255);
            e.Property(x => x.ImageURL).HasColumnName("image_url").HasMaxLength(255);
        });

        b.Entity<VariantOption>(e =>
        {
            e.ToTable("store_variant_option");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.VariantTypeId).HasColumnName("variant_type_id");
            e.Property(x => x.Value).HasColumnName("value").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.MetaValue).HasColumnName("meta_value").HasMaxLength(255);
            // cascade = ALL, orphanRemoval = true on VariantType.options
            e.HasOne(x => x.VariantType).WithMany(t => t.Options).HasForeignKey(x => x.VariantTypeId).OnDelete(DeleteBehavior.ClientCascade);
        });

        b.Entity<VariantType>(e =>
        {
            e.ToTable("store_variant_type");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(20);
        });

        b.Entity<VideoReel>(e =>
        {
            e.ToTable("store_video_reel");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            e.Property(x => x.VideoUrl).HasColumnName("video_url").HasColumnType("text");
            e.Property(x => x.ThumbnailUrl).HasColumnName("thumbnail_url").HasColumnType("text").IsRequired(false);
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(255).IsRequired(false);
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp(6) without time zone");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
