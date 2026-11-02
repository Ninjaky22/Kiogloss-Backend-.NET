// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Reglas del panel de administración: crear, editar y borrar productos, etiquetas y variantes, y consultar usuarios y pedidos.
// ======================================

using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Dtos.Admin;
using Kiogloss.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Services;

/// <summary>Reglas del panel admin: crear, ver, editar y borrar productos, imágenes, etiquetas y variantes, y consultar usuarios y pedidos.</summary>
public sealed class AdminService(KioglossDbContext db, CloudinaryService cloudinaryService)
{
    // ==================== PRODUCT MANAGEMENT ====================

    /// <summary>Crea un producto con sus variantes, atributos e imágenes en una sola transacción.</summary>
    public async Task<ProductAdminResponse> CreateProduct(ProductCreateRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = new Product
        {
            Title = request.Title!,
            Price = request.Price!.Value,
            Description = request.Description,
            Slug = request.Slug!,
            Status = request.Status ?? "draft",
            Published = DateTime.Now,
        };

        // product.setAttributes(request.getAttributes()) keeps the request's LinkedHashMap (insertion order)
        Dictionary<string, string?> attributes = new();
        if (request.Attributes != null)
        {
            attributes = request.Attributes;
            foreach (var (name, value) in request.Attributes)
                product.AttributeRows.Add(new ProductAttribute { Name = name, Value = value });
        }

        if (request.Variants is { Count: > 0 })
        {
            foreach (var vReq in request.Variants)
            {
                if (vReq == null) throw JavaErrors.NullPointer();
                var variant = new ProductVariant { Product = product };
                if (vReq.OptionIds is { Count: > 0 })
                    foreach (var option in await FindOptionsById(vReq.OptionIds)) variant.Options.Add(option);
                variant.Stock = vReq.Stock ?? 0;
                variant.Sku = vReq.Sku;
                variant.ImageUrl = vReq.ImageUrl;
                product.Variants.Add(variant);
            }
        }
        else
        {
            // Auto-create default variant (no options) so stock/cart always works via variantId
            product.Variants.Add(new ProductVariant { Product = product, Stock = request.Stock ?? 0 });
        }

        List<Tag> tagsOrder = [];
        if (request.TagIds is { Count: > 0 })
        {
            // product.setTags(new HashSet<>(tagRepository.findAllById(ids)))
            var tags = await FindTagsById(request.TagIds);
            foreach (var tag in tags) product.Tags.Add(tag);
            tagsOrder = JavaHash.CopiedOrder(tags, t => t.JavaHashCode());
        }

        db.Products.Add(product);
        await db.SaveChangesAsync();
        var response = await ConvertToAdminResponse(product, attributes, tagsOrder);
        await tx.CommitAsync();
        return response;
    }

    /// <summary>Actualiza un producto y sincroniza sus variantes, atributos e imágenes.</summary>
    public async Task<ProductAdminResponse> UpdateProduct(long productId, ProductUpdateRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = await LoadProductGraph(productId) ?? throw new Compat.RuntimeException("Product not found");

        if (request.Title != null) product.Title = request.Title;
        if (request.Price != null) product.Price = request.Price.Value;
        if (request.Description != null) product.Description = request.Description;
        if (request.Slug != null) product.Slug = request.Slug;
        if (request.Status != null) product.Status = request.Status;
        // the loaded PersistentMap keeps its table when cleared and refilled with putAll(request map)
        var attributes = product.AttributesMap();
        if (request.Attributes != null)
        {
            var tableSize = JavaHash.TableSizeFor(JavaHash.HibernateLoadedCapacity(product.AttributeRows.Count));
            foreach (var row in product.AttributeRows.ToList()) product.AttributeRows.Remove(row);
            await db.SaveChangesAsync();
            foreach (var (name, value) in request.Attributes)
                product.AttributeRows.Add(new ProductAttribute { ProductId = product.Id, Name = name, Value = value });
            attributes = JavaHash.HashOrder(request.Attributes.Keys.ToList(), JavaHash.Of, tableSize)
                .ToDictionary(k => k, k => request.Attributes[k]);
        }

        if (request.Variants is { Count: > 0 })
        {
            var updatedVariantIds = request.Variants.Where(v => v?.Id != null).Select(v => v!.Id!.Value).ToHashSet();
            foreach (var v in product.Variants.Where(v => !updatedVariantIds.Contains(v.Id)).ToList())
                RemoveVariant(product, v);

            foreach (var vReq in request.Variants)
            {
                if (vReq == null) throw JavaErrors.NullPointer();
                ProductVariant? variant;
                if (vReq.Id != null)
                {
                    variant = product.Variants.FirstOrDefault(v => v.Id == vReq.Id.Value);
                    if (variant == null) continue;
                }
                else
                {
                    variant = new ProductVariant { Product = product };
                    product.Variants.Add(variant);
                }

                if (vReq.OptionIds != null)
                {
                    variant.Options.Clear();
                    foreach (var option in await FindOptionsById(vReq.OptionIds)) variant.Options.Add(option);
                }
                variant.Stock = vReq.Stock ?? 0;
                variant.Sku = vReq.Sku;
                variant.ImageUrl = vReq.ImageUrl;
            }
        }
        else if (request.Variants != null)
        {
            // Variants list is explicitly empty → ensure a default variant exists
            // Remove all real variants (those with options), keep or create default
            foreach (var v in product.Variants.Where(v => v.Options.Count > 0).ToList())
                RemoveVariant(product, v);

            if (product.Variants.Count == 0)
            {
                product.Variants.Add(new ProductVariant { Product = product, Stock = request.Stock ?? 0 });
            }
            else
            {
                // Update the existing default variant's stock
                var existing = product.VariantsInSetOrder().First();
                existing.Stock = request.Stock ?? existing.Stock;
            }
        }

        var tagsOrder = product.TagsInSetOrder();
        if (request.TagIds != null)
        {
            var tags = await FindTagsById(request.TagIds);
            product.Tags.Clear();
            foreach (var tag in tags) product.Tags.Add(tag);
            tagsOrder = JavaHash.CopiedOrder(tags, t => t.JavaHashCode());
        }

        await db.SaveChangesAsync();
        var response = await ConvertToAdminResponse(product, attributes, tagsOrder);
        await tx.CommitAsync();
        return response;
    }

    /// <summary>Elimina un producto y sus recursos asociados.</summary>
    public async Task DeleteProduct(long productId)
    {
        var product = await LoadProductGraph(productId) ?? throw new Compat.RuntimeException("Product not found");
        db.Products.Remove(product);
        await db.SaveChangesAsync();
    }

    /// <summary>Lista paginada de productos para el panel admin, con búsqueda opcional.</summary>
    public async Task<SpringPage<ProductAdminResponse>> GetAllProducts(string? search, PageRequest pageable)
    {
        IQueryable<Product> query = db.Products;
        if (search != null)
        {
            // searchByStatus(null, search, pageable)
            var pattern = ("%" + search + "%").ToLower();
            query = query.Where(p =>
                EF.Functions.Like(p.Title.ToLower(), pattern)
                || p.Tags.Any(t => EF.Functions.Like(t.Name.ToLower(), pattern))
                || p.Variants.Any(v => v.Options.Any(o => EF.Functions.Like(o.Value.ToLower(), pattern))));
        }
        var page = await query.OrderByDescending(p => p.Published).ThenByDescending(p => p.Id).ToSpringPageAsync(pageable);
        var content = new List<ProductAdminResponse>();
        foreach (var p in page.Content) content.Add(await ConvertToAdminResponse(p, p.AttributesMap(), p.TagsInSetOrder()));
        return new SpringPage<ProductAdminResponse>(content, pageable, page.TotalElements);
    }

    /// <summary>Obtiene un producto por id; lanza <see cref="ResourceNotFoundException"/> si no existe.</summary>
    public async Task<ProductAdminResponse> GetProductById(long productId)
    {
        var product = await db.Products.FindAsync(productId) ?? throw new Compat.RuntimeException("Product not found");
        return await ConvertToAdminResponse(product, product.AttributesMap(), product.TagsInSetOrder());
    }

    // ==================== IMAGE MANAGEMENT ====================

    /// <summary>Sube una imagen y la asocia a un producto.</summary>
    public async Task UploadProductImage(ProductImageUploadRequest request)
    {
        var product = await db.Products.FindAsync(request.ProductId!.Value) ?? throw new Compat.RuntimeException("Product not found");
        var imageUrl = await cloudinaryService.UploadBase64Image(request.ImageBase64!);
        db.Images.Add(new Images { ProductId = product.Id, Image = imageUrl });
        await db.SaveChangesAsync();
    }

    /// <summary>Elimina una imagen de producto (y su archivo si es local).</summary>
    public async Task DeleteProductImage(long imageId)
    {
        var image = await db.Images.FindAsync(imageId) ?? throw new Compat.RuntimeException("Image not found");
        db.Images.Remove(image);
        await db.SaveChangesAsync();
    }

    // ==================== TAG MANAGEMENT ====================

    /// <summary>Crea una etiqueta.</summary>
    public async Task<TagDTO> CreateTag(TagCreateRequest request)
    {
        var tag = new Tag { Name = request.Name!, ImageURL = request.ImageURL };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return new TagDTO(tag.Id, tag.Name, tag.ImageURL);
    }

    /// <summary>Actualiza una etiqueta existente.</summary>
    public async Task<TagDTO> UpdateTag(long tagId, TagUpdateRequest request)
    {
        var tag = await db.Tags.FindAsync(tagId) ?? throw new Compat.RuntimeException("Tag not found");
        if (request.Name != null) tag.Name = request.Name;
        if (request.ImageURL != null) tag.ImageURL = request.ImageURL;
        await db.SaveChangesAsync();
        return new TagDTO(tag.Id, tag.Name, tag.ImageURL);
    }

    /// <summary>Elimina una etiqueta.</summary>
    public async Task DeleteTag(long tagId)
    {
        // Tag is not the owner of the many-to-many: Hibernate only deletes the tag row, so a tag still
        // used by a product fails on the foreign key (-> 409), exactly like before.
        var tag = await db.Tags.FindAsync(tagId) ?? throw new Compat.RuntimeException("Tag not found");
        await db.Tags.Where(t => t.Id == tag.Id).ExecuteDeleteAsync();
    }

    /// <summary>Lista todas las etiquetas.</summary>
    public async Task<List<TagDTO>> GetAllTagsAdmin() =>
        (await db.Tags.OrderBy(t => t.Id).ToListAsync()).Select(t => new TagDTO(t.Id, t.Name, t.ImageURL)).ToList();

    // ==================== VARIANT MANAGEMENT ====================

    /// <summary>Crea un tipo de variante con sus opciones.</summary>
    public async Task<VariantTypeDTO> CreateVariantType(VariantTypeCreateRequest request)
    {
        var type = new VariantType { Name = request.Name!, Type = request.Type };
        if (request.Options != null)
        {
            foreach (var optReq in request.Options)
            {
                if (optReq == null) throw JavaErrors.NullPointer();
                type.Options.Add(new VariantOption { VariantType = type, Value = optReq.Value!, MetaValue = optReq.MetaValue });
            }
        }
        db.VariantTypes.Add(type);
        await db.SaveChangesAsync();
        return ConvertToVariantTypeDTO(type);
    }

    /// <summary>Actualiza un tipo de variante y sus opciones.</summary>
    public async Task<VariantTypeDTO> UpdateVariantType(long id, VariantTypeCreateRequest request)
    {
        var type = await db.VariantTypes.Include(t => t.Options).FirstOrDefaultAsync(t => t.Id == id)
                   ?? throw new Compat.RuntimeException("VariantType not found");
        type.Name = request.Name!;
        type.Type = request.Type;

        // Smart merge: keep existing options that are in use by products
        var existingOptionIds = type.Options.Select(o => o.Id).ToHashSet();
        var inUseIds = existingOptionIds.Count == 0 ? new HashSet<long>() : await FindOptionIdsInUse(existingOptionIds);

        // Build a map of requested options by id (for matching existing ones)
        var requestById = new Dictionary<long, VariantOptionCreateRequest>();
        var newOptions = new List<VariantOptionCreateRequest>();
        if (request.Options != null)
        {
            foreach (var optReq in request.Options)
            {
                if (optReq == null) throw JavaErrors.NullPointer();
                if (optReq.Id != null && existingOptionIds.Contains(optReq.Id.Value))
                    requestById[optReq.Id.Value] = optReq;
                else
                    newOptions.Add(optReq);
            }
        }

        // Remove options not in the request AND not in use
        foreach (var opt in type.Options.ToList())
        {
            if (requestById.ContainsKey(opt.Id)) continue; // keep: still in request
            if (inUseIds.Contains(opt.Id)) continue; // keep: in use by products
            type.Options.Remove(opt); // safe to remove (orphanRemoval)
            db.VariantOptions.Remove(opt);
        }

        // Update existing options that are still in the request
        foreach (var opt in type.Options)
        {
            if (requestById.TryGetValue(opt.Id, out var req))
            {
                opt.Value = req.Value!;
                opt.MetaValue = req.MetaValue;
            }
        }

        // Add new options
        foreach (var optReq in newOptions)
            type.Options.Add(new VariantOption { VariantType = type, Value = optReq.Value!, MetaValue = optReq.MetaValue });

        await db.SaveChangesAsync();
        return ConvertToVariantTypeDTO(type);
    }

    /// <summary>Lista todos los tipos de variante con sus opciones.</summary>
    public async Task<List<VariantTypeDTO>> GetAllVariantTypes() =>
        (await db.VariantTypes.OrderBy(t => t.Id).ToListAsync()).Select(ConvertToVariantTypeDTO).ToList();

    /// <summary>Elimina un tipo de variante.</summary>
    public async Task DeleteVariantType(long id)
    {
        var type = await db.VariantTypes.Include(t => t.Options).FirstOrDefaultAsync(t => t.Id == id)
                   ?? throw new Compat.RuntimeException("Tipo de variante no encontrado");

        var optionIds = type.Options.Select(o => o.Id).ToHashSet();
        if (optionIds.Count > 0)
        {
            var inUseIds = await FindOptionIdsInUse(optionIds);
            if (inUseIds.Count > 0)
            {
                throw new Compat.RuntimeException(
                    "No se puede eliminar este tipo de variante porque algunas opciones están asociadas a productos existentes. " +
                    "Elimine primero las variantes de los productos que usan estas opciones.");
            }
        }

        db.VariantTypes.Remove(type);
        await db.SaveChangesAsync();
    }

    private static VariantTypeDTO ConvertToVariantTypeDTO(VariantType type) =>
        new(type.Id, type.Name, type.Type, type.Options.OrderBy(o => o.Id == 0 ? long.MaxValue : o.Id)
            .Select(o => new VariantOptionDTO(o.Id, o.VariantType.Name, o.Value, o.MetaValue)).ToList());

    // ==================== HELPER METHODS ====================

    private async Task<ProductAdminResponse> ConvertToAdminResponse(Product product, Dictionary<string, string?> attributes,
        List<Tag> tagsInSetOrder)
    {
        var images = await db.Images.Where(i => i.ProductId == product.Id).ToListAsync();
        var variants = product.VariantsInSetOrder();

        var variantDTOs = variants
            .Where(v => v.Options.Count > 0) // Exclude default variants (no options)
            .Select(v => new ProductVariantDTO(v.Id,
                v.OptionsInSetOrder().Select(o => new VariantOptionDTO(o.Id, o.VariantType.Name, o.Value, o.MetaValue)).ToList(),
                v.Stock, v.Sku, v.ImageUrl))
            .ToList();

        // Calculate stock from default variant (variant with no options)
        var defaultStock = variants.FirstOrDefault(v => v.Options.Count == 0)?.Stock;

        return new ProductAdminResponse
        {
            Id = product.Id,
            Title = product.Title,
            Price = JavaFormat.BigDecimal(product.Price),
            Description = product.Description,
            Slug = product.Slug,
            Status = product.Status,
            Published = JavaFormat.LocalDateTimeToString(product.Published),
            Attributes = attributes,
            Stock = defaultStock,
            Variants = variantDTOs,
            Tags = tagsInSetOrder.Select(t => new TagDTO(t.Id, t.Name, t.ImageURL)).ToList(),
            Images = images.Select(img => new ImageDTO(img.Id, cloudinaryService.GetImageUrl(img.Image))).ToList(),
        };
    }

    /// <summary>Loads the product with every collection Hibernate cascades to (needed for orphan removal / delete).</summary>
    private Task<Product?> LoadProductGraph(long productId) =>
        db.Products
            .Include(p => p.Variants).ThenInclude(v => v.Options)
            .Include(p => p.Images)
            .Include(p => p.AttributeRows)
            .Include(p => p.Tags)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == productId);

    /// <summary>orphanRemoval of a variant: Hibernate deletes its option join rows and the variant row.</summary>
    private void RemoveVariant(Product product, ProductVariant variant)
    {
        product.Variants.Remove(variant);
        variant.Options.Clear();
        db.ProductVariants.Remove(variant);
    }

    private async Task<List<VariantOption>> FindOptionsById(List<long?> ids)
    {
        var values = ids.Where(i => i != null).Select(i => i!.Value).ToList();
        return await db.VariantOptions.Where(o => values.Contains(o.Id)).ToListAsync();
    }

    private async Task<List<Tag>> FindTagsById(List<long?> ids)
    {
        var values = ids.Where(i => i != null).Select(i => i!.Value).ToList();
        return await db.Tags.Where(t => values.Contains(t.Id)).ToListAsync();
    }

    /// <summary>ProductVariantRepository#findOptionIdsInUse</summary>
    private async Task<HashSet<long>> FindOptionIdsInUse(HashSet<long> optionIds) =>
        (await db.ProductVariants.SelectMany(pv => pv.Options).Where(o => optionIds.Contains(o.Id))
            .Select(o => o.Id).Distinct().ToListAsync()).ToHashSet();
}
