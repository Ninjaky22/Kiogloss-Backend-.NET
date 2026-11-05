// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Direcciones (rutas) que solo puede usar un administrador: /admin/...
// Sirven para manejar productos, etiquetas, usuarios, pedidos, videos y reportes.
// Estos archivos solo reciben la petición y responden. Las reglas del negocio están en la carpeta Services.
// ======================================

using Kiogloss.Api.Compat;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Dtos.Admin;
using Kiogloss.Api.Mappers;
using Kiogloss.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kiogloss.Api.Controllers;

/// <summary>
/// Port of com.todoteg.controller.AdminController (@PreAuthorize("hasRole('ADMIN')") is enforced for
/// the whole /admin/** tree by UrlAuthorizationMiddleware, as SecurityConfig did).
/// </summary>
[Route("admin")]
public sealed class AdminController(
    AdminService adminService,
    UserService userService,
    OrderService orderService,
    VideoService videoService,
    FileStorageService fileStorageService,
    UserMapper userMapper) : JavaControllerBase
{
    private const string Ctl = " com.todoteg.controller.AdminController";

    // ==================== FILE UPLOAD ====================

    /// <summary>POST /admin/upload - Sube un archivo y devuelve su URL pública.</summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile()
    {
        if (!Request.HasFormContentType || !(Request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) ?? false))
            throw JavaErrors.NotMultipart();
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file") ?? throw JavaErrors.MissingPart("file");
        var filename = await fileStorageService.Store(file);
        // ServletUriComponentsBuilder.fromCurrentContextPath().path("/uploads/").path(filename)
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/uploads/{filename}";
        return Json(new Dictionary<string, string> { ["url"] = url });
    }

    // ==================== PRODUCT ENDPOINTS ====================

    /// <summary>POST /admin/products - Crea un producto con sus variantes e imágenes.</summary>
    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct()
    {
        var request = await ReadBody<ProductCreateRequest>(
            Spring + "<com.todoteg.dto.admin.ProductAdminResponse>" + Ctl + ".createProduct(com.todoteg.dto.admin.ProductCreateRequest)",
            "productCreateRequest");
        return Json(await adminService.CreateProduct(request), 201);
    }

    /// <summary>GET /admin/products - Lista paginada de productos (búsqueda opcional con `search`).</summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetAllProducts()
    {
        var search = OptionalParam("search");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 20);
        var pageable = PageRequest.Of(page, pageSize, sorted: true); // Sort.by("published").descending()
        return Json(await adminService.GetAllProducts(search, pageable));
    }

    /// <summary>GET /admin/products/{id} - Obtiene un producto por id.</summary>
    [HttpGet("products/{id}")]
    public async Task<IActionResult> GetProductById(string id) => Json(await adminService.GetProductById(PathLong("id", id)));

    /// <summary>PUT /admin/products/{id} - Actualiza un producto existente.</summary>
    [HttpPut("products/{id}")]
    public async Task<IActionResult> UpdateProduct(string id)
    {
        var productId = PathLong("id", id);
        var request = await ReadBody<ProductUpdateRequest>(
            Spring + "<com.todoteg.dto.admin.ProductAdminResponse>" + Ctl + ".updateProduct(java.lang.Long,com.todoteg.dto.admin.ProductUpdateRequest)",
            "productUpdateRequest");
        return Json(await adminService.UpdateProduct(productId, request));
    }

    /// <summary>DELETE /admin/products/{id} - Elimina un producto.</summary>
    [HttpDelete("products/{id}")]
    public async Task<IActionResult> DeleteProduct(string id)
    {
        await adminService.DeleteProduct(PathLong("id", id));
        return EmptyStatus(204);
    }

    // ==================== IMAGE ENDPOINTS ====================

    /// <summary>POST /admin/products/images - Sube una imagen asociada a un producto.</summary>
    [HttpPost("products/images")]
    public async Task<IActionResult> UploadProductImage()
    {
        var request = await ReadBody<ProductImageUploadRequest>(
            Spring + "<java.lang.Void>" + Ctl + ".uploadProductImage(com.todoteg.dto.admin.ProductImageUploadRequest) throws java.lang.Exception",
            "productImageUploadRequest");
        await adminService.UploadProductImage(request);
        return EmptyStatus(201);
    }

    /// <summary>DELETE /admin/products/images/{id} - Elimina la imagen de un producto.</summary>
    [HttpDelete("products/images/{id}")]
    public async Task<IActionResult> DeleteProductImage(string id)
    {
        await adminService.DeleteProductImage(PathLong("id", id));
        return EmptyStatus(204);
    }

    // ==================== TAG ENDPOINTS ====================

    /// <summary>POST /admin/tags - Crea una etiqueta.</summary>
    [HttpPost("tags")]
    public async Task<IActionResult> CreateTag()
    {
        var request = await ReadBody<TagCreateRequest>(
            Spring + "<com.todoteg.dto.TagDTO>" + Ctl + ".createTag(com.todoteg.dto.admin.TagCreateRequest)", "tagCreateRequest");
        return Json(await adminService.CreateTag(request), 201);
    }

    /// <summary>GET /admin/tags - Lista todas las etiquetas.</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetAllTags() => Json(await adminService.GetAllTagsAdmin());

    /// <summary>PUT /admin/tags/{id} - Actualiza una etiqueta.</summary>
    [HttpPut("tags/{id}")]
    public async Task<IActionResult> UpdateTag(string id)
    {
        var tagId = PathLong("id", id);
        var request = await ReadBody<TagUpdateRequest>(
            Spring + "<com.todoteg.dto.TagDTO>" + Ctl + ".updateTag(java.lang.Long,com.todoteg.dto.admin.TagUpdateRequest)", "tagUpdateRequest");
        return Json(await adminService.UpdateTag(tagId, request));
    }

    /// <summary>DELETE /admin/tags/{id} - Elimina una etiqueta.</summary>
    [HttpDelete("tags/{id}")]
    public async Task<IActionResult> DeleteTag(string id)
    {
        await adminService.DeleteTag(PathLong("id", id));
        return EmptyStatus(204);
    }

    // ==================== VARIANT MANAGEMENT ====================

    /// <summary>POST /admin/variants/types - Crea un tipo de variante con sus opciones.</summary>
    [HttpPost("variants/types")]
    public async Task<IActionResult> CreateVariantType()
    {
        var request = await ReadBody<VariantTypeCreateRequest>(
            Spring + "<com.todoteg.dto.VariantTypeDTO>" + Ctl + ".createVariantType(com.todoteg.dto.admin.VariantTypeCreateRequest)",
            "variantTypeCreateRequest");
        return Json(await adminService.CreateVariantType(request), 201);
    }

    /// <summary>GET /admin/variants/types - Lista los tipos de variante.</summary>
    [HttpGet("variants/types")]
    public async Task<IActionResult> GetAllVariantTypes() => Json(await adminService.GetAllVariantTypes());

    /// <summary>PUT /admin/variants/types/{id} - Actualiza un tipo de variante y sus opciones.</summary>
    [HttpPut("variants/types/{id}")]
    public async Task<IActionResult> UpdateVariantType(string id)
    {
        var typeId = PathLong("id", id);
        var request = await ReadBody<VariantTypeCreateRequest>(
            Spring + "<com.todoteg.dto.VariantTypeDTO>" + Ctl + ".updateVariantType(java.lang.Long,com.todoteg.dto.admin.VariantTypeCreateRequest)",
            "variantTypeCreateRequest");
        return Json(await adminService.UpdateVariantType(typeId, request));
    }

    /// <summary>DELETE /admin/variants/types/{id} - Elimina un tipo de variante.</summary>
    [HttpDelete("variants/types/{id}")]
    public async Task<IActionResult> DeleteVariantType(string id)
    {
        await adminService.DeleteVariantType(PathLong("id", id));
        return EmptyStatus(204);
    }

    // ============= GESTIÓN DE USUARIOS =============

    /// <summary>GET /admin/users - Lista paginada de usuarios para el panel, con búsqueda y filtros.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var search = OptionalParam("search");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 10);
        var sortBy = Param("sortBy", "name");
        var sortDir = Param("sortDir", "ASC");

        var descending = sortDir.Equals("DESC", StringComparison.OrdinalIgnoreCase);
        var pageable = PageRequest.Of(page, pageSize, sorted: true);

        var usersPage = await userService.FindAllUsers(search, pageable, sortBy, descending);
        var dtoPage = userMapper.ToUserDashboardPage(usersPage);
        return Json(ApiResponseDTO<PageResponseDTO<UserDashboardDTO>>.Ok(dtoPage));
    }

    /// <summary>Obtener detalles completos de un usuario</summary>
    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUserDetails(string id)
    {
        var user = await userService.FindById(PathLong("id", id));
        return Json(ApiResponseDTO<UserDetailDTO>.Ok(userMapper.ToUserDetailDTO(user)));
    }

    /// <summary>Actualizar roles de un usuario</summary>
    [HttpPut("users/{id}/roles")]
    public async Task<IActionResult> UpdateUserRoles(string id)
    {
        var userId = PathLong("id", id);
        var updateDTO = await ReadBody<UpdateUserRolesDTO>(
            Spring + "<com.todoteg.dto.admin.ApiResponseDTO<com.todoteg.dto.admin.UserDashboardDTO>>" + Ctl +
            ".updateUserRoles(java.lang.Long,com.todoteg.dto.admin.UpdateUserRolesDTO)");
        var updatedUser = await userService.UpdateUserRoles(userId, updateDTO);
        return Json(ApiResponseDTO<UserDashboardDTO>.Ok("Roles actualizados correctamente", userMapper.ToUserDashboardDTO(updatedUser)));
    }

    /// <summary>Activar/Desactivar usuario</summary>
    [HttpPatch("users/{id}/toggle-status")]
    public async Task<IActionResult> ToggleUserStatus(string id)
    {
        var user = await userService.ToggleUserStatus(PathLong("id", id));
        return Json(ApiResponseDTO<UserDashboardDTO>.Ok("Estado del usuario actualizado", userMapper.ToUserDashboardDTO(user)));
    }

    // ============= GESTIÓN DE ÓRDENES =============

    /// <summary>Listar todas las órdenes (con filtros): GET /admin/orders?status=PENDING&amp;page=0&amp;page_size=20</summary>
    [HttpGet("orders")]
    public async Task<IActionResult> GetAllOrders()
    {
        var status = OptionalParam("status");
        var userId = OptionalLongParam("userId");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 20);
        var pageable = PageRequest.Of(page, pageSize, sorted: true);

        var ordersPage = await orderService.FindAllOrders(status, userId, pageable);
        return Json(ApiResponseDTO<PageResponseDTO<OrderSummaryDTO>>.Ok(userMapper.ToOrderSummaryPage(ordersPage)));
    }

    /// <summary>Obtener detalles completos de una orden</summary>
    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrderDetails(string id)
    {
        var order = await orderService.FindById(PathLong("id", id));
        return Json(ApiResponseDTO<OrderDetailDTO>.Ok(userMapper.ToOrderDetailDTO(order)));
    }

    /// <summary>Obtener órdenes de un usuario específico</summary>
    [HttpGet("users/{userId}/orders")]
    public async Task<IActionResult> GetUserOrders(string userId)
    {
        var id = PathLong("userId", userId);
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 10);
        var pageable = PageRequest.Of(page, pageSize, sorted: true);

        var ordersPage = await orderService.FindOrdersByUserId(id, pageable);
        return Json(ApiResponseDTO<PageResponseDTO<OrderSummaryDTO>>.Ok(userMapper.ToOrderSummaryPage(ordersPage)));
    }

    /// <summary>Actualizar estado de una orden: PATCH /admin/orders/456/status?status=SHIPPED</summary>
    [HttpPatch("orders/{id}/status")]
    public async Task<IActionResult> UpdateOrderStatus(string id)
    {
        var orderId = PathLong("id", id);
        var status = RequiredParam("status");
        var order = await orderService.UpdateOrderStatus(orderId, status);
        return Json(ApiResponseDTO<OrderSummaryDTO>.Ok("Estado de orden actualizado", userMapper.ToOrderSummaryDTO(order)));
    }

    // ============= ESTADÍSTICAS DEL DASHBOARD =============

    /// <summary>GET /admin/stats - Estadísticas del dashboard (hoy solo usuarios totales y activos).</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats() =>
        Json(ApiResponseDTO<DashboardStatsDTO>.Ok(await userService.GetDashboardStats()));

    // ==================== VIDEO ENDPOINTS ====================

    /// <summary>GET /admin/videos - Lista todos los videos, publicados o no.</summary>
    [HttpGet("videos")]
    public async Task<IActionResult> GetAllVideos() => Json(await videoService.GetAllVideos());

    /// <summary>POST /admin/videos - Crea un video.</summary>
    [HttpPost("videos")]
    public async Task<IActionResult> CreateVideo()
    {
        var request = await ReadBody<VideoCreateRequest>(
            Spring + "<com.todoteg.dto.VideoReelDTO>" + Ctl + ".createVideo(com.todoteg.dto.VideoCreateRequest)", "videoCreateRequest");
        return Json(await videoService.CreateVideo(request), 201);
    }

    /// <summary>PUT /admin/videos/{id} - Actualiza un video.</summary>
    [HttpPut("videos/{id}")]
    public async Task<IActionResult> UpdateVideo(string id)
    {
        var videoId = PathLong("id", id);
        var request = await ReadBody<VideoUpdateRequest>(
            Spring + "<com.todoteg.dto.VideoReelDTO>" + Ctl + ".updateVideo(java.lang.Long,com.todoteg.dto.VideoUpdateRequest)");
        return Json(await videoService.UpdateVideo(videoId, request));
    }

    /// <summary>DELETE /admin/videos/{id} - Elimina un video.</summary>
    [HttpDelete("videos/{id}")]
    public async Task<IActionResult> DeleteVideo(string id)
    {
        await videoService.DeleteVideo(PathLong("id", id));
        return EmptyStatus(204);
    }
}

/// <summary>Reportes del panel admin (/admin/reports/...): resumen, ventas por día, productos más vendidos y pedidos por estado.</summary>
[Route("admin/reports")]
public sealed class ReportController(ReportService reportService) : JavaControllerBase
{
    /// <summary>KPIs: ingresos totales, órdenes totales, productos activos, ingresos y órdenes de hoy.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary() => Json(await reportService.GetSummary());

    /// <summary>Ventas agrupadas por día (por defecto los últimos 30 días).</summary>
    [HttpGet("sales-by-day")]
    public async Task<IActionResult> GetSalesByDay() => Json(await reportService.GetSalesByDay(IntParam("days", 30)));

    /// <summary>Productos más vendidos por cantidad (por defecto el top 5).</summary>
    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts() => Json(await reportService.GetTopProducts(IntParam("limit", 5), IntParam("days", 0)));

    /// <summary>Detalle del periodo: KPIs, comparación con el periodo previo, desglose por estado y mejores clientes.</summary>
    [HttpGet("period-detail")]
    public async Task<IActionResult> GetPeriodDetail() => Json(await reportService.GetPeriodDetail(IntParam("days", 30)));

    /// <summary>Distribución de órdenes por estado para la gráfica Donut.</summary>
    [HttpGet("orders-by-status")]
    public async Task<IActionResult> GetOrderStatusDistribution() => Json(await reportService.GetOrderStatusDistribution());
}
