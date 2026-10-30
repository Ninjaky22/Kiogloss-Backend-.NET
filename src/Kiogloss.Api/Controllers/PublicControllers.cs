using Kiogloss.Api.Compat;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Security;
using Kiogloss.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kiogloss.Api.Controllers;

/// <summary>Port of com.todoteg.controller.AuthController.</summary>
public sealed class AuthController(AuthService authService, PasswordResetService passwordResetService) : JavaControllerBase
{
    private const string Ctl = " com.todoteg.controller.AuthController";

    /// <summary>POST /login - Genera access y refresh tokens</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        var request = await ReadBody<LoginRequest>(
            Spring + "<com.todoteg.dto.TokenResponse>" + Ctl + ".login(com.todoteg.dto.LoginRequest)", "loginRequest");
        return Json(await authService.Login(request));
    }

    /// <summary>POST /refresh - Genera nuevo access token a partir del refresh token</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var request = await ReadBody<RefreshTokenRequest>(
            Spring + "<com.todoteg.dto.TokenResponse>" + Ctl + ".refresh(com.todoteg.dto.RefreshTokenRequest)", "refreshTokenRequest");
        return Json(await authService.Refresh(request.Refresh));
    }

    /// <summary>POST /auth/forgot-password — siempre responde 200 para no revelar si el email existe.</summary>
    [HttpPost("auth/forgot-password")]
    public async Task<IActionResult> ForgotPassword()
    {
        var request = await ReadBody<ForgotPasswordRequest>(
            Spring + "<java.util.Map<java.lang.String, java.lang.String>>" + Ctl + ".forgotPassword(com.todoteg.dto.ForgotPasswordRequest)",
            "forgotPasswordRequest");
        await passwordResetService.RequestReset(request.Email);
        return Json(new Dictionary<string, string>
        {
            ["message"] = "Si el email existe, recibirás un enlace para restablecer tu contraseña",
        });
    }

    /// <summary>POST /auth/reset-password — Body: { "token": "...", "newPassword": "..." }</summary>
    [HttpPost("auth/reset-password")]
    public async Task<IActionResult> ResetPassword()
    {
        var request = await ReadBody<ResetPasswordRequest>(
            Spring + "<java.util.Map<java.lang.String, java.lang.String>>" + Ctl + ".resetPassword(com.todoteg.dto.ResetPasswordRequest)",
            "resetPasswordRequest");
        await passwordResetService.ConfirmReset(request.Token, request.NewPassword);
        return Json(new Dictionary<string, string> { ["message"] = "Contraseña restablecida exitosamente" });
    }
}

/// <summary>Port of com.todoteg.controller.ProductController.</summary>
public sealed class ProductController(ProductService productService, VideoService videoService) : JavaControllerBase
{
    /// <summary>GET /products - Listar productos publicados con paginación y búsqueda</summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        var search = OptionalParam("search");
        var tags = OptionalParam("tags");
        var sort = OptionalParam("sort");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 8);

        var sortMode = sort switch
        {
            "price-low" => ProductService.SortMode.PriceLow,
            "price-high" => ProductService.SortMode.PriceHigh,
            _ => ProductService.SortMode.Latest, // "latest" o por defecto
        };
        var pageable = PageRequest.Of(page, pageSize, sorted: true);

        List<string>? tagList = null;
        if (!string.IsNullOrWhiteSpace(tags))
            tagList = JavaSplit(tags, ',');

        return Json(await productService.GetPublishedProducts(search, tagList, sortMode, pageable));
    }

    /// <summary>GET /article/{slug} - Obtener detalle de producto por slug</summary>
    [HttpGet("article/{slug}")]
    public async Task<IActionResult> GetProductDetail(string slug) => Json(await productService.GetProductBySlug(slug));

    /// <summary>GET /tags - Listar todas las categorías/tags</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags() => Json(await productService.GetAllTags());

    [HttpGet("videos")]
    public async Task<IActionResult> GetAllVideos() => Json(await videoService.GetPublishedVideos());

    /// <summary>String#split(regex): trailing empty strings are removed.</summary>
    private static List<string> JavaSplit(string value, char separator)
    {
        var parts = value.Split(separator).ToList();
        while (parts.Count > 1 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return parts;
    }
}

/// <summary>Port of com.todoteg.controller.UserController.</summary>
[Route("user")]
public sealed class UserController(UserService userService, AuthService authService, JwtUtil jwtUtil) : JavaControllerBase
{
    private const string Ctl = " com.todoteg.controller.UserController";

    /// <summary>GET /user/ — public (permitAll "/user/") paginated list of UserProfile entities.</summary>
    [HttpGet("")]
    public async Task<IActionResult> FindAllUsers()
    {
        var search = OptionalParam("search");
        var page = IntParam("page", 0);
        var pageSize = IntParam("page_size", 8);
        var pageable = PageRequest.Of(page, pageSize, sorted: true);
        var users = await userService.FindAllUsers(search, pageable, "name", descending: true);
        return Json(users.Map(ToEntityJson));
    }

    /// <summary>POST /user/ - Crear nuevo usuario y retornar tokens JWT</summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateUser()
    {
        var request = await ReadBody<UserCreateRequest>(
            Spring + "<com.todoteg.dto.TokenResponse>" + Ctl + ".createUser(com.todoteg.dto.UserCreateRequest) throws java.lang.Exception",
            "userCreateRequest");
        await userService.CreateUser(request);
        var tokens = await authService.Login(new LoginRequest { Email = request.Email, Password = request.Password });
        return Json(tokens, 201);
    }

    /// <summary>GET /user/{pk} - Obtener detalle del usuario autenticado (valida que el token corresponda).</summary>
    [HttpGet("{pk}")]
    public async Task<IActionResult> GetUserDetail(string pk)
    {
        var id = PathLong("pk", pk);
        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            var tokenUserId = jwtUtil.ExtractUserId(authHeader[7..]) ?? throw JavaErrors.NullPointer();
            // Validar que el usuario del token coincida con el solicitado
            if (tokenUserId != id)
                return Json(new ErrorResponse("Acceso no autorizado"), 401);
        }
        return Json(await userService.GetUserDetail(id));
    }

    /// <summary>PATCH /user/update/{pk} - Actualizar usuario parcialmente</summary>
    [HttpPatch("update/{pk}")]
    public async Task<IActionResult> UpdateUser(string pk)
    {
        var id = PathLong("pk", pk);
        var request = await ReadBody<UserUpdateRequest>(
            Spring + "<java.lang.Void>" + Ctl +
            ".updateUser(java.lang.Long,com.todoteg.dto.UserUpdateRequest,org.springframework.security.core.Authentication) throws java.lang.Exception",
            "userUpdateRequest");
        await userService.UpdateUser(id, request);
        return EmptyStatus(204);
    }

    private static UserProfileJson ToEntityJson(Models.UserProfile u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        Name = u.Name,
        IsActive = u.IsActive,
        IsStaff = u.IsStaff,
        IsSuperuser = u.IsSuperuser,
        ProfileImage = u.ProfileImage,
        PhoneNumber = u.PhoneNumber,
        Account = u.Account == null
            ? null
            : new AccountJson
            {
                Id = u.Account.Id,
                Address = u.Account.Address == null
                    ? null
                    : new AddressJson
                    {
                        Id = u.Account.Address.Id,
                        Street = u.Account.Address.Street,
                        StreetNumber = u.Account.Address.StreetNumber,
                        Distric = u.Account.Address.Distric,
                        City = u.Account.Address.City,
                    },
                PointsPerPurchase = u.Account.PointsPerPurchase,
                IsActive = u.Account.IsActive,
            },
        Username = u.Email,
        Authorities = u.GetAuthorities().Select(a => new AuthorityJson(a)).ToList(),
        Enabled = u.IsEnabled(),
    };
}

/// <summary>Port of com.todoteg.controller.FavoriteController.</summary>
public sealed class FavoriteController(FavoriteService favoriteService) : JavaControllerBase
{
    /// <summary>POST /user/favorite - Agregar producto a favoritos</summary>
    [HttpPost("user/favorite")]
    public async Task<IActionResult> AddFavorite()
    {
        var request = await ReadBody<FavoriteCreateRequest>(
            Spring + "<java.util.Map<java.lang.String, java.lang.Long>> com.todoteg.controller.FavoriteController.addFavorite(com.todoteg.dto.FavoriteCreateRequest)",
            "favoriteCreateRequest");
        var idFa = await favoriteService.AddFavorite(request);
        return Json(new Dictionary<string, long> { ["idFa"] = idFa }, 201);
    }

    /// <summary>DELETE /user/delete/{pk} - Eliminar favorito</summary>
    [HttpDelete("user/delete/{pk}")]
    public async Task<IActionResult> RemoveFavorite(string pk)
    {
        await favoriteService.RemoveFavorite(PathLong("pk", pk));
        return EmptyStatus(204);
    }

    /// <summary>GET /user/account/account_favorites/{id} - Obtener favoritos de una cuenta</summary>
    [HttpGet("user/account/account_favorites/{id}")]
    public async Task<IActionResult> GetAccountFavorites(string id) =>
        Json(await favoriteService.GetAccountFavorites(PathLong("id", id)));
}
